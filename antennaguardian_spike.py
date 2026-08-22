#!/usr/bin/env python3
"""Throwaway Flex Ethernet interlock bench spike.

This prototype answers one question: can a dynamic ANT interlock prevent a
CAT-originated PTT request from reaching RF when the client withholds ready?
It deliberately contains no command path that sends ``interlock ready``.
"""

from __future__ import annotations

import argparse
import datetime as dt
import re
import socket
import sys
import time
from dataclasses import dataclass
from pathlib import Path


FLEX_PORT = 4992
TOKEN_RE = re.compile(r"^[A-Za-z0-9_+.-]+$")
RESPONSE_RE = re.compile(r"^R(?P<seq>\d+)\|(?P<code>[0-9A-Fa-f]+)(?:\|(?P<body>.*))?$")


@dataclass
class SessionState:
    mode: str
    interlock_id: str | None = None
    create_sequence: int | None = None
    remove_sequence: int | None = None
    not_ready_sequence: int | None = None
    block_armed: bool = False
    ptt_requests: int = 0
    transmit_events: int = 0
    cleanup_confirmed: bool = False


class Transcript:
    def __init__(self, path: Path) -> None:
        path.parent.mkdir(parents=True, exist_ok=True)
        self.path = path
        self._file = path.open("a", encoding="utf-8", buffering=1)

    def write(self, direction: str, message: str) -> None:
        stamp = dt.datetime.now().astimezone().isoformat(timespec="milliseconds")
        line = f"{stamp} {direction:<6} {message}"
        print(line, flush=True)
        self._file.write(line + "\n")

    def close(self) -> None:
        self._file.close()


class FlexSpike:
    def __init__(
        self,
        host: str,
        antennas: list[str],
        mode: str,
        transcript: Transcript,
        duration: float | None,
    ) -> None:
        self.host = host
        self.antennas = antennas
        self.state = SessionState(mode=mode)
        self.transcript = transcript
        self.duration = duration
        self.socket: socket.socket | None = None
        self.sequence = 0
        self.pending: dict[int, str] = {}
        self.buffer = b""

    def next_sequence(self) -> int:
        self.sequence += 1
        return self.sequence

    def send_command(self, command: str) -> int:
        if self.socket is None:
            raise RuntimeError("radio socket is not connected")
        sequence = self.next_sequence()
        frame = f"C{sequence}|{command}\n"
        self.socket.sendall(frame.encode("ascii"))
        self.pending[sequence] = command
        self.transcript.write("SEND", frame.rstrip())
        return sequence

    def run(self) -> int:
        self.transcript.write("STATE", f"mode={self.state.mode} radio={self.host}:{FLEX_PORT}")
        with socket.create_connection((self.host, FLEX_PORT), timeout=5.0) as radio_socket:
            self.socket = radio_socket
            radio_socket.settimeout(0.5)
            self.transcript.write("STATE", "tcp_connected=1")

            self.send_command("name AntennaGuardian-Spike")
            self.send_command("sub radio all")
            self.send_command("sub slice all")
            self.send_command("sub tx all")

            if self.state.mode == "block":
                antenna_list = ",".join(self.antennas)
                self.state.create_sequence = self.send_command(
                    # Flex's wiki alternates between model= and name=. This radio
                    # rejects model= with SL_RESP_UNKNOWN; its API generation uses
                    # the older, also officially documented name= form.
                    "interlock create type=ANT name=AntennaGuardian-Spike "
                    f"serial=prototype valid_antennas={antenna_list}"
                )

            started = time.monotonic()
            try:
                while self.duration is None or time.monotonic() - started < self.duration:
                    self.read_once()
            except KeyboardInterrupt:
                self.transcript.write("STATE", "operator_stop=1")
            finally:
                self.remove_interlock()
                self.socket = None

        self.print_verdict()
        return 0

    def read_once(self) -> None:
        assert self.socket is not None
        try:
            chunk = self.socket.recv(65536)
        except socket.timeout:
            return
        if not chunk:
            raise ConnectionError("radio closed the TCP connection")
        self.buffer += chunk
        while b"\n" in self.buffer or b"\r" in self.buffer:
            split_at = min(
                index for index in (self.buffer.find(b"\n"), self.buffer.find(b"\r")) if index >= 0
            )
            raw_line = self.buffer[:split_at]
            self.buffer = self.buffer[split_at + 1 :]
            self.buffer = self.buffer.lstrip(b"\r\n")
            line = raw_line.decode("utf-8", errors="replace").strip()
            if line:
                self.handle_line(line)

    def handle_line(self, line: str) -> None:
        self.transcript.write("RECV", line)
        response = RESPONSE_RE.match(line)
        if response:
            self.handle_response(
                int(response.group("seq")),
                int(response.group("code"), 16),
                response.group("body") or "",
            )
            return

        if "|interlock " not in line:
            return
        if "state=PTT_REQUESTED" in line:
            self.state.ptt_requests += 1
            self.transcript.write(
                "STATE",
                f"ptt_requested={self.state.ptt_requests} action=WITHHOLD_READY",
            )
        if "state=TRANSMITTING" in line:
            self.state.transmit_events += 1
            severity = "FAIL" if self.state.block_armed else "OBSERVED"
            self.transcript.write(
                "STATE",
                f"transmitting={self.state.transmit_events} verdict={severity}",
            )

    def handle_response(self, sequence: int, code: int, body: str) -> None:
        command = self.pending.pop(sequence, "<unknown>")
        self.transcript.write(
            "STATE",
            f"response_seq={sequence} success={int(code == 0)} command={command!r}",
        )
        if code != 0:
            raise RuntimeError(f"radio rejected {command!r} with response 0x{code:08X}")

        if sequence == self.state.create_sequence:
            interlock_id = body.split("|", 1)[0].strip()
            if not interlock_id:
                raise RuntimeError("radio accepted interlock create but returned no interlock ID")
            self.state.interlock_id = interlock_id
            self.state.not_ready_sequence = self.send_command(
                f"interlock not_ready {interlock_id}"
            )
            self.transcript.write(
                "STATE",
                f"interlock_id={interlock_id} block_armed=0 awaiting_not_ready_ack=1",
            )
        elif sequence == self.state.not_ready_sequence:
            self.state.block_armed = True
            self.transcript.write(
                "STATE",
                "block_armed=1 ready_command_available=0 test_ptt_now=1",
            )
        elif sequence == self.state.remove_sequence:
            self.state.cleanup_confirmed = True
            self.transcript.write("STATE", "interlock_removed=1")

    def remove_interlock(self) -> None:
        if self.socket is None or self.state.interlock_id is None:
            return
        try:
            self.state.remove_sequence = self.send_command(
                f"interlock remove {self.state.interlock_id}"
            )
            deadline = time.monotonic() + 2.0
            while not self.state.cleanup_confirmed and time.monotonic() < deadline:
                self.read_once()
        except (ConnectionError, OSError, RuntimeError) as error:
            self.transcript.write("WARN", f"interlock_cleanup_unconfirmed={error}")

    def print_verdict(self) -> None:
        if self.state.mode == "observe":
            verdict = "Observation complete; no interlock was created."
        elif not self.state.block_armed:
            verdict = "INCONCLUSIVE: the radio never confirmed not-ready."
        elif self.state.ptt_requests == 0:
            verdict = "INCONCLUSIVE: block armed, but no PTT_REQUESTED event was observed."
        elif self.state.transmit_events == 0:
            verdict = "PASS CANDIDATE: PTT was requested and no TRANSMITTING state followed."
        else:
            verdict = "FAIL: the radio reported TRANSMITTING while the block was armed."
        self.transcript.write("VERDICT", verdict)


def parse_antennas(value: str) -> list[str]:
    antennas = [item.strip() for item in value.split(",") if item.strip()]
    if not antennas:
        raise argparse.ArgumentTypeError("provide at least one antenna token")
    invalid = [item for item in antennas if not TOKEN_RE.fullmatch(item)]
    if invalid:
        raise argparse.ArgumentTypeError(f"invalid antenna token: {invalid[0]!r}")
    return antennas


def default_log_path() -> Path:
    stamp = dt.datetime.now().strftime("%Y%m%d-%H%M%S")
    return Path(__file__).resolve().parent / "logs" / f"interlock-{stamp}.log"


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Observe or fail-closed test a Flex dynamic antenna interlock."
    )
    parser.add_argument("radio", help="Flex radio IPv4 address or hostname")
    parser.add_argument(
        "--mode",
        choices=("observe", "block"),
        default="observe",
        help="observe only, or create an ANT interlock that never sends ready",
    )
    parser.add_argument(
        "--antennas",
        type=parse_antennas,
        help="comma-separated Flex antenna tokens; required in block mode",
    )
    parser.add_argument(
        "--confirm-block-test",
        action="store_true",
        help="required acknowledgment that block mode intentionally refuses PTT",
    )
    parser.add_argument(
        "--duration",
        type=float,
        help="optional run time in seconds; otherwise stop with Ctrl+C",
    )
    parser.add_argument("--log", type=Path, default=None, help="transcript path")
    return parser


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()
    if args.duration is not None and args.duration <= 0:
        parser.error("--duration must be greater than zero")
    if args.mode == "block":
        if args.antennas is None:
            parser.error("--antennas is required in block mode")
        if not args.confirm_block_test:
            parser.error("block mode requires --confirm-block-test")
    elif args.antennas is not None:
        parser.error("--antennas only applies to block mode")

    transcript = Transcript((args.log or default_log_path()).resolve())
    try:
        transcript.write("STATE", f"transcript={transcript.path}")
        spike = FlexSpike(
            host=args.radio,
            antennas=args.antennas or [],
            mode=args.mode,
            transcript=transcript,
            duration=args.duration,
        )
        return spike.run()
    except (ConnectionError, OSError, RuntimeError) as error:
        transcript.write("ERROR", str(error))
        return 1
    finally:
        transcript.close()


if __name__ == "__main__":
    sys.exit(main())
