#!/usr/bin/env python3
"""
Simulates an external application (such as Baballonia) asking the VRCFT Babble module to (re)initialize.

Sends the OSC message /vrcft/initialize with two booleans (eye tracking, face tracking) over UDP to the
module's OSC port. The module only accepts it from this machine.

  During the module's 10 s startup wait, the message decides what it claims.
  After that, a request that differs from the current one makes VRCFaceTracking reinitialize all modules.

Keys:
  e  toggle eye tracking        f  toggle face tracking
  1  eye only   2  face only    3  eye + face   0  neither   (each sends at once)
  space / enter  send the current selection
  q / esc        quit

Standard library only. Usage: python simulate_init.py [--host 127.0.0.1] [--port 8888]
"""

import argparse
import socket
import sys

INIT_ADDRESS = "/vrcft/initialize"


def osc_string(s: str) -> bytes:
    """An OSC string: ASCII, null-terminated, padded to a multiple of 4 bytes."""
    data = s.encode("ascii") + b"\0"
    return data + b"\0" * (-len(data) % 4)


def init_message(eye: bool, face: bool) -> bytes:
    """/vrcft/initialize ,TF style: OSC booleans are type tags only, with no argument bytes."""
    tags = "," + ("T" if eye else "F") + ("T" if face else "F")
    return osc_string(INIT_ADDRESS) + osc_string(tags)


def read_key():
    """Blocks for one keypress and returns it as a string."""
    if sys.platform == "win32":
        import msvcrt

        ch = msvcrt.getwch()
        if ch in ("\x00", "\xe0"):  # arrow/function keys come as two codes; ignore them
            msvcrt.getwch()
            return ""
        return ch

    import termios
    import tty

    fd = sys.stdin.fileno()
    old = termios.tcgetattr(fd)
    try:
        tty.setraw(fd)
        return sys.stdin.read(1)
    finally:
        termios.tcsetattr(fd, termios.TCSADRAIN, old)


def on_off(value: bool) -> str:
    return "on " if value else "off"


def main() -> None:
    parser = argparse.ArgumentParser(description="Send (re)initialization requests to the VRCFT Babble module.")
    parser.add_argument("--host", default="127.0.0.1", help="module host (default 127.0.0.1)")
    parser.add_argument("--port", type=int, default=8888, help="module OSC port (default 8888)")
    args = parser.parse_args()

    target = (args.host, args.port)
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    eye, face = False, True  # the module's own fallback

    def show() -> None:
        print(f"\r  selection: eye {on_off(eye)}  face {on_off(face)}   ", end="", flush=True)

    def send() -> None:
        packet = init_message(eye, face)
        sock.sendto(packet, target)
        print(f"\r  sent {INIT_ADDRESS} eye={eye} face={face} to {target[0]}:{target[1]} ({len(packet)} bytes)")
        show()

    print(__doc__.split("Standard library")[0].rstrip())
    print(f"\nTarget: {target[0]}:{target[1]}\n")
    show()

    presets = {"1": (True, False), "2": (False, True), "3": (True, True), "0": (False, False)}
    try:
        while True:
            key = read_key().lower()
            if key in ("q", "\x1b", "\x03"):  # q, esc, ctrl+c
                break
            if key == "e":
                eye = not eye
                show()
            elif key == "f":
                face = not face
                show()
            elif key in presets:
                eye, face = presets[key]
                send()
            elif key in (" ", "\r", "\n"):
                send()
    except KeyboardInterrupt:
        pass
    finally:
        sock.close()
        print()


if __name__ == "__main__":
    main()
