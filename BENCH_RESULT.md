# Flex Ethernet Interlock Bench Result

Date: 2026-08-22

## Question

Can a standalone Flex API client prevent a software-originated PTT request
from entering RF by registering a dynamic antenna interlock and withholding
`interlock ready`?

## Verdict

**Yes.** The radio emitted `PTT_REQUESTED` twice with `source=SW`. The spike
withheld `ready`, the radio emitted `Interlock is preventing transmission`,
and no `TRANSMITTING` state was observed. The radio accepted removal of the
dynamic interlock and a follow-up observation confirmed normal `READY` state
with `tx_allowed=1`.

This proves the radio-level mechanism needed for AntennaGuardian. It does not
yet prove a production policy engine, reconnect behavior, every PTT source, or
every antenna port.

## Test Configuration

- SmartSDR TCP/IP API port: 4992
- API protocol prologue: 1.4.0.0
- Active TX frequency: 14.302 MHz
- Active TX antenna: ANT1
- Interlock coverage: ANT1, ANT2
- Interlock type: ANT
- Client behavior: explicitly sent `not_ready`; never sent `ready`

## Compatibility Finding

The Flex wiki alternates between `model=` and `name=` in dynamic interlock
examples. This radio rejected `model=` with `SL_RESP_UNKNOWN` (`0x50001000`)
but accepted the older documented `name=` form and returned interlock ID `1`.
The spike now uses `name=`.

## Evidence

- `logs/radio-observe.log`: read-only protocol and status validation
- `logs/radio-block-test.log`: rejected `model=` attempt
- `logs/radio-post-reject-check.log`: confirmation that no stale interlock remained
- `logs/radio-block-test-name.log`: successful block test
- `logs/radio-final-cleanup-check.log`: successful cleanup and return to READY

## Next Build Step

Replace the fixed withhold behavior with a fail-closed policy decision:

1. Track the radio-authoritative TX slice frequency and TX antenna.
2. Map frequency to amateur band.
3. Evaluate an explicit antenna-by-band allow matrix.
4. Send `interlock ready` only for an allowed combination.
5. Return to `not_ready` after unkey, state loss, reconnect, or policy change.
6. Expose armed, allowed, blocked, degraded, and disconnected states clearly.
