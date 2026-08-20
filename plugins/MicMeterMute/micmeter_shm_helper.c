/*
    micmeter_shm_helper.c — shared-memory helper for the MicMeter app.

    The MicMeter app (C#) uses shm_open/mmap through this tiny C bridge, because
    macOS exports shm_open as a variadic function and ftruncate with an
    $INODE64 symbol variant, both of which are awkward to call correctly via a
    direct C# P/Invoke. Doing it in C removes that fragility.
*/

#include <sys/mman.h>
#include <sys/stat.h>
#include <fcntl.h>
#include <unistd.h>
#include <stddef.h>

#define SHM_NAME   "/micmeter_shared_ring"   /* shm_open requires a leading slash */
#define LEGACY_SHM_NAME "/micmeter_shared"

/* Mirrors struct MicMeterShared in micmeter_mute.c:
   float peak (4) + int mute (4) + uint sample_rate (4) + uint write_frame (4) +
   uint read_frame (4) + float buffer[65536*2] (524288) + uint ring_frames (4).
   The ring geometry is fixed; see the RING_FRAMES note in micmeter_mute.c. */
#define SHM_SIZE (20 + 65536 * 2 * 4 + 4)

/* Maps the shared block, creating it if needed. Returns MAP_FAILED on failure. */
void* micmeter_shm_map(int* out_fd)
{
    /* The first public build used an 8-byte object under the legacy name that
       macOS refuses to grow (EINVAL); drop it once since it is never reused. */
    (void)shm_unlink(LEGACY_SHM_NAME);

    int fd = shm_open(SHM_NAME, O_RDWR | O_CREAT, 0666);
    if (fd >= 0) {
        /* Grow-only: shrinking would SIGBUS a process still mapped to the
           larger layout from an older build. */
        struct stat st;
        off_t need = (off_t)SHM_SIZE;
        if (fstat(fd, &st) == 0 && st.st_size < need) {
            ftruncate(fd, need);
        }
    }

    if (fd < 0) {
        if (out_fd) *out_fd = -1;
        return MAP_FAILED;
    }

    void* p = mmap(NULL, SHM_SIZE, PROT_READ | PROT_WRITE, MAP_SHARED, fd, 0);
    if (p == MAP_FAILED) {
        close(fd);
        if (out_fd) *out_fd = -1;
        return MAP_FAILED;
    }

    if (out_fd) *out_fd = fd;
    return p;
}

/* Unmaps and closes the shared block. */
void micmeter_shm_unmap(void* ptr, int fd)
{
    if (ptr != MAP_FAILED && ptr != NULL) {
        munmap(ptr, SHM_SIZE);
    }
    if (fd >= 0) {
        close(fd);
    }
}
