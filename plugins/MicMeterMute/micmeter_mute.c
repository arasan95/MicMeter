/*
    micmeter_mute.c — MicMeter Mute / Level LV2 plugin

    A minimal LV2 audio plugin that (a) passes stereo audio through or silences
    it based on a shared-memory mute flag written by the MicMeter app,
    (b) publishes the input peak level back to MicMeter through the same
    shared-memory block, and (c) streams the interleaved stereo input into a
    shared-memory ring buffer that the MicMeter app plays back when its "listen"
    control is enabled.

    Shared memory layout (name "micmeter_shared"):
        volatile float peak;          // plugin -> MicMeter (input peak, 0..1)
        volatile int   mute;          // MicMeter -> plugin (0 = pass, 1 = silence)
        volatile unsigned int sample_rate;  // plugin -> MicMeter (host sample rate)
        volatile unsigned int write_frame;  // plugin -> MicMeter (release store)
        volatile unsigned int read_frame;   // MicMeter -> plugin (consumer position)
        float buffer[RING_FRAMES * 2];      // interleaved stereo input frames

    The ring is single-producer/single-consumer. The writer uses an atomic
    release store so the reader observes fully-written frames; on overflow the
    writer drops the oldest block by advancing the consumer position, which is
    acceptable for a monitor/listen signal.

    This file is self-contained: it defines the small subset of the LV2 API
    needed to build an effect plugin, so no external LV2 SDK is required.
*/

#include <math.h>
#include <string.h>
#include <stdint.h>
#include <stdlib.h>
#include <sys/mman.h>
#include <sys/stat.h>
#include <fcntl.h>
#include <unistd.h>

/* ============================================================================
   Minimal LV2 core declarations (subset of lv2.h / lv2core.h, BSD licensed)
   ============================================================================ */

typedef void* LV2_Handle;
typedef void* LV2_Feature;

/* Port type URIs */
#define LV2_CORE__AudioPort   "http://lv2plug.in/ns/lv2core#AudioPort"
#define LV2_CORE__InputPort   "http://lv2plug.in/ns/lv2core#InputPort"
#define LV2_CORE__OutputPort  "http://lv2plug.in/ns/lv2core#OutputPort"
#define LV2_CORE__ControlPort "http://lv2plug.in/ns/lv2core#ControlPort"

/* Feature URIs (unused by this plugin, declared for completeness) */
#define LV2_DATA_ACCESS_URI "http://lv2plug.in/ns/ext/data-access"

/* Forward declaration so the instantiate signature can reference the descriptor. */
typedef struct _LV2_Descriptor LV2_Descriptor;

typedef void (*LV2_Connect_Port_Function)(
    LV2_Handle instance, uint32_t port, void* data_location);

typedef LV2_Handle (*LV2_Instantiate_Function)(
    const LV2_Descriptor* descriptor,
    double rate, const char* bundle_path,
    const LV2_Feature* const* features);

typedef void (*LV2_Cleanup_Function)(LV2_Handle instance);
typedef void (*LV2_Activate_Function)(LV2_Handle instance);
typedef void (*LV2_Deactivate_Function)(LV2_Handle instance);

typedef void (*LV2_Run_Function)(LV2_Handle instance, uint32_t sample_count);

typedef const void* (*LV2_Extension_Data_Function)(const char* uri);

struct _LV2_Descriptor {
    const char*                 URI;
    LV2_Instantiate_Function    instantiate;
    LV2_Connect_Port_Function   connect_port;
    LV2_Activate_Function       activate;
    LV2_Run_Function            run;
    LV2_Deactivate_Function     deactivate;
    LV2_Cleanup_Function        cleanup;
    LV2_Extension_Data_Function extension_data;
};

#if defined(__GNUC__)
#define LV2_SYMBOL_EXPORT __attribute__((visibility("default")))
#else
#define LV2_SYMBOL_EXPORT
#endif

/* ============================================================================
   Plugin constants
   ============================================================================ */

#define PLUGIN_URI "urn:arasan95:micmeter:mute"
#define SHM_NAME   "/micmeter_shared_ring"   /* shm_open requires a leading slash */
#define LEGACY_SHM_NAME "/micmeter_shared"

/* Ring of ~1.36 s of stereo audio at 48 kHz (65536 frames * 2ch * 4B).
   MUST stay fixed: the ring geometry is shared in the memory block and a host
   may still hold an older build of this plugin (loaded before an update), so
   changing the size would make app and plugin use different masks and produce
   aliased/echoing playback. Effective listen latency is NOT set by this
   capacity — the app resets the consumer to the live edge (with a small slip)
   when listening starts. */
#define RING_FRAMES 65536

enum {
    PORT_INPUT_L   = 0,
    PORT_INPUT_R   = 1,
    PORT_OUTPUT_L  = 2,
    PORT_OUTPUT_R  = 3
};

/* ============================================================================
   Shared memory
   ============================================================================ */

struct MicMeterShared {
    volatile float peak;
    volatile int   mute;
    volatile unsigned int sample_rate;
    volatile unsigned int write_frame;
    volatile unsigned int read_frame;
    float buffer[RING_FRAMES * 2];
    /* Appended after the buffer (grow-only allocation) so older builds that map
       a smaller block are unaffected. Lets the app verify it agrees with this
       plugin on the ring geometry instead of guessing. */
    volatile unsigned int ring_frames;
};

typedef struct {
    const float* input_l;
    const float* input_r;
    float*       output_l;
    float*       output_r;

    struct MicMeterShared* shared;
    int shm_fd;
} Plugin;

/* ============================================================================
   Lifecycle
   ============================================================================ */

static LV2_Handle instantiate(
    const LV2_Descriptor* descriptor,
    double rate,
    const char* bundle_path,
    const LV2_Feature* const* features)
{
    (void)descriptor;
    (void)bundle_path;
    (void)features;

    Plugin* plugin = (Plugin*)calloc(1, sizeof(Plugin));
    if (!plugin) {
        return NULL;
    }

    plugin->shm_fd = -1;
    plugin->shared = NULL;

    /* Drop the 8-byte object from the first public build once; macOS will not
       grow it, which otherwise breaks every later map of the ring. */
    (void)shm_unlink(LEGACY_SHM_NAME);

    /* O_CREAT always (creates if absent, opens if present). Only grow when the
       object is smaller than we need: shrinking it would SIGBUS any process
       still running an older build that maps the larger layout. */
    int fd = shm_open(SHM_NAME, O_RDWR | O_CREAT, 0666);
    if (fd >= 0) {
        struct stat st;
        off_t need = (off_t)sizeof(struct MicMeterShared);
        if (fstat(fd, &st) == 0 && st.st_size < need) {
            ftruncate(fd, need);
        }
    }

    if (fd >= 0) {
        struct MicMeterShared* sh =
            (struct MicMeterShared*)mmap(NULL, sizeof(struct MicMeterShared),
                                         PROT_READ | PROT_WRITE, MAP_SHARED, fd, 0);
        if (sh != MAP_FAILED) {
            plugin->shm_fd = fd;
            plugin->shared = sh;
            __atomic_store_n(&sh->sample_rate, (unsigned int)rate, __ATOMIC_RELAXED);
            __atomic_store_n(&sh->ring_frames, (unsigned int)RING_FRAMES, __ATOMIC_RELAXED);
        } else {
            close(fd);
        }
    }

    return (LV2_Handle)plugin;
}

static void cleanup(LV2_Handle instance)
{
    Plugin* plugin = (Plugin*)instance;
    if (!plugin) {
        return;
    }

    if (plugin->shared) {
        munmap(plugin->shared, sizeof(struct MicMeterShared));
    }
    if (plugin->shm_fd >= 0) {
        close(plugin->shm_fd);
    }

    free(plugin);
}

static void connect_port(LV2_Handle instance, uint32_t port, void* data_location)
{
    Plugin* plugin = (Plugin*)instance;
    if (!plugin) {
        return;
    }

    switch (port) {
        case PORT_INPUT_L:  plugin->input_l  = (const float*)data_location; break;
        case PORT_INPUT_R:  plugin->input_r  = (const float*)data_location; break;
        case PORT_OUTPUT_L: plugin->output_l = (float*)data_location;       break;
        case PORT_OUTPUT_R: plugin->output_r = (float*)data_location;       break;
        default: break;
    }
}

static void activate(LV2_Handle instance)   { (void)instance; }
static void deactivate(LV2_Handle instance) { (void)instance; }

/* ============================================================================
   DSP
   ============================================================================ */

static void run(LV2_Handle instance, uint32_t sample_count)
{
    Plugin* plugin = (Plugin*)instance;
    if (!plugin || !plugin->input_l || !plugin->input_r ||
        !plugin->output_l || !plugin->output_r) {
        return;
    }

    struct MicMeterShared* sh = plugin->shared;
    int mute = sh ? sh->mute : 0;

    float peak = 0.0f;
    const float* l = plugin->input_l;
    const float* r = plugin->input_r;

    if (sh && sample_count > 0) {
        /* Stream the interleaved stereo input into the shared ring. */
        unsigned int w = __atomic_load_n(&sh->write_frame, __ATOMIC_RELAXED);
        unsigned int rd = __atomic_load_n(&sh->read_frame, __ATOMIC_RELAXED);
        if ((uint32_t)(w - rd) >= RING_FRAMES) {
            /* Ring full: drop the oldest block so the reader keeps fresh audio. */
            rd = w - RING_FRAMES + 1;
            __atomic_store_n(&sh->read_frame, rd, __ATOMIC_RELAXED);
        }

        unsigned int idx = w & (RING_FRAMES - 1);
        float* buf = sh->buffer;
        const unsigned int mask = RING_FRAMES - 1;
        for (uint32_t i = 0; i < sample_count; ++i) {
            float a = fabsf(l[i]);
            float b = fabsf(r[i]);
            if (a > peak) peak = a;
            if (b > peak) peak = b;
            buf[(idx << 1) + 0] = l[i];
            buf[(idx << 1) + 1] = r[i];
            idx = (idx + 1) & mask;
        }

        /* Release store: the reader must only observe frames after the
           corresponding samples have been written. */
        __atomic_store_n(&sh->write_frame, w + sample_count, __ATOMIC_RELEASE);
        sh->peak = peak;
    } else {
        for (uint32_t i = 0; i < sample_count; ++i) {
            float a = fabsf(l[i]);
            float b = fabsf(r[i]);
            if (a > peak) peak = a;
            if (b > peak) peak = b;
        }
        if (sh) {
            sh->peak = peak;
        }
    }

    if (mute) {
        memset(plugin->output_l, 0, sample_count * sizeof(float));
        memset(plugin->output_r, 0, sample_count * sizeof(float));
    } else {
        memcpy(plugin->output_l, l, sample_count * sizeof(float));
        memcpy(plugin->output_r, r, sample_count * sizeof(float));
    }
}

/* ============================================================================
   Descriptor
   ============================================================================ */

static const LV2_Descriptor descriptor = {
    PLUGIN_URI,
    instantiate,
    connect_port,
    activate,
    run,
    deactivate,
    cleanup,
    NULL
};

LV2_SYMBOL_EXPORT
const LV2_Descriptor* lv2_descriptor(uint32_t index)
{
    return index == 0 ? &descriptor : NULL;
}
