// test_shim.c — Validates the generic verifrog_sim API against the counter module
//
// Build: see Makefile test target
// This file is for development validation only, not shipped.

#include <stdio.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>

// Forward declarations matching verifrog_sim extern "C" API
typedef struct SimContext SimContext;
typedef struct Checkpoint Checkpoint;

extern SimContext* sim_create(void);
extern void sim_destroy(SimContext* ctx);
extern void sim_reset(SimContext* ctx, int cycles);
extern void sim_step(SimContext* ctx, int n);
extern uint64_t sim_get_cycle(SimContext* ctx);
extern int sim_read(SimContext* ctx, const char* name, int64_t* out);
extern int sim_write(SimContext* ctx, const char* name, int64_t value);
extern int sim_signal_bits(SimContext* ctx, const char* name);
extern int sim_signal_count(SimContext* ctx);
extern int sim_signal_name(SimContext* ctx, int index, char* buf, int buf_len);
extern void sim_suppress_display(int suppress);
extern Checkpoint* sim_checkpoint(SimContext* ctx);
extern int sim_restore(SimContext* ctx, Checkpoint* cp);
extern void sim_checkpoint_free(Checkpoint* cp);
extern uint64_t sim_checkpoint_cycle(Checkpoint* cp);
extern int sim_force(SimContext* ctx, const char* name, int64_t value);
extern int sim_release(SimContext* ctx, const char* name);
extern void sim_release_all(SimContext* ctx);
extern int sim_force_count(SimContext* ctx);

#define PASS(msg) printf("  PASS: %s\n", msg)
#define FAIL(msg, ...) do { printf("  FAIL: " msg "\n", ##__VA_ARGS__); failures++; } while(0)

int main(void) {
    int failures = 0;

    printf("=== verifrog_sim shim test (counter module) ===\n\n");

    // Test 1: Create and destroy
    printf("Test 1: Create/Destroy\n");
    SimContext* sim = sim_create();
    if (sim) PASS("sim_create returned non-null");
    else { FAIL("sim_create returned null"); return 1; }

    // Test 2: Reset
    printf("Test 2: Reset\n");
    sim_suppress_display(1);
    sim_reset(sim, 10);
    uint64_t cycle = sim_get_cycle(sim);
    if (cycle == 0) PASS("cycle count is 0 after reset");
    else FAIL("cycle count is %llu after reset, expected 0", cycle);

    // Test 3: Read count after reset (should be 0)
    printf("Test 3: Read signal after reset\n");
    int64_t count_val = -1;
    int rc = sim_read(sim, "count", &count_val);
    if (rc == 0 && count_val == 0) PASS("count = 0 after reset");
    else FAIL("count read: rc=%d, value=%lld (expected 0)", rc, count_val);

    // Test 4: Write enable, step, verify count increments
    printf("Test 4: Enable and step\n");
    sim_write(sim, "enable", 1);
    sim_step(sim, 5);
    rc = sim_read(sim, "count", &count_val);
    if (rc == 0 && count_val == 5) PASS("count = 5 after 5 steps with enable=1");
    else FAIL("count = %lld after 5 steps, expected 5 (rc=%d)", count_val, rc);

    // Test 5: Signal bits
    printf("Test 5: Signal bits\n");
    int bits = sim_signal_bits(sim, "count");
    if (bits == 8) PASS("count is 8 bits");
    else FAIL("count bits = %d, expected 8", bits);

    bits = sim_signal_bits(sim, "overflow");
    if (bits == 1) PASS("overflow is 1 bit");
    else FAIL("overflow bits = %d, expected 1", bits);

    bits = sim_signal_bits(sim, "nonexistent");
    if (bits == -1) PASS("nonexistent signal returns -1");
    else FAIL("nonexistent signal bits = %d, expected -1", bits);

    // Test 6: Checkpoint/Restore
    printf("Test 6: Checkpoint/Restore\n");
    Checkpoint* cp = sim_checkpoint(sim);
    if (cp) PASS("checkpoint created");
    else { FAIL("checkpoint returned null"); goto cleanup; }

    uint64_t cp_cycle = sim_checkpoint_cycle(cp);
    if (cp_cycle == 5) PASS("checkpoint cycle = 5");
    else FAIL("checkpoint cycle = %llu, expected 5", cp_cycle);

    // Step more, then restore
    sim_step(sim, 10);
    rc = sim_read(sim, "count", &count_val);
    printf("  (after 10 more steps: count = %lld, cycle = %llu)\n", count_val, sim_get_cycle(sim));

    rc = sim_restore(sim, cp);
    if (rc == 0) PASS("restore succeeded");
    else FAIL("restore returned %d", rc);

    rc = sim_read(sim, "count", &count_val);
    if (rc == 0 && count_val == 5) PASS("count = 5 after restore");
    else FAIL("count = %lld after restore, expected 5", count_val);

    if (sim_get_cycle(sim) == 5) PASS("cycle = 5 after restore");
    else FAIL("cycle = %llu after restore, expected 5", sim_get_cycle(sim));

    sim_checkpoint_free(cp);

    // Test 7: Force/Release
    printf("Test 7: Force/Release\n");
    sim_force(sim, "enable", 0);
    if (sim_force_count(sim) == 1) PASS("force count = 1");
    else FAIL("force count = %d, expected 1", sim_force_count(sim));

    sim_step(sim, 5);
    rc = sim_read(sim, "count", &count_val);
    if (rc == 0 && count_val == 5) PASS("count unchanged with enable forced to 0");
    else FAIL("count = %lld, expected 5 (enable forced off)", count_val);

    sim_release(sim, "enable");
    if (sim_force_count(sim) == 0) PASS("force released");
    else FAIL("force count = %d after release, expected 0", sim_force_count(sim));

    // Test 8: Signal enumeration
    printf("Test 8: Signal enumeration\n");
    int sig_count = sim_signal_count(sim);
    if (sig_count > 0) {
        printf("  PASS: signal count = %d\n", sig_count);
        char buf[256];
        for (int i = 0; i < sig_count && i < 10; i++) {
            int len = sim_signal_name(sim, i, buf, 256);
            if (len > 0) printf("    [%d] %s\n", i, buf);
        }
    } else {
        FAIL("signal count = 0");
    }

    // Test 9: Load value via write
    printf("Test 9: Load value\n");
    sim_write(sim, "load_value", 42);
    sim_write(sim, "load_en", 1);
    sim_step(sim, 1);
    sim_write(sim, "load_en", 0);
    rc = sim_read(sim, "count", &count_val);
    if (rc == 0 && count_val == 42) PASS("count = 42 after load");
    else FAIL("count = %lld after load, expected 42 (rc=%d)", count_val, rc);

cleanup:
    sim_destroy(sim);

    printf("\n=== Results: %d failure(s) ===\n", failures);
    return failures > 0 ? 1 : 0;
}
