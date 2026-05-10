"""
bluetooth_service.py — Bluetooth device scanning.

Two operations:
  scan(mac)   → check if a specific MAC is nearby
  pick()      → discover any nearby named device (for registration)
"""

import re

try:
    import bluetooth as _bt
    _BT_OK = True
except ImportError:
    _bt   = None
    _BT_OK = False

_MAC_RE = re.compile(r'^([0-9A-Fa-f]{2}[:\-]){5}[0-9A-Fa-f]{2}$')


def _check_available() -> str | None:
    """Return error string if bluetooth is unavailable, else None."""
    if not _BT_OK:
        return "ERROR:PyBluez not installed (pip install pybluez2)"
    return None


def scan(target_mac: str) -> str:
    """
    Scan for target_mac.
    Returns: FOUND:<name>:<mac>  |  NOT_FOUND  |  ERROR:<msg>
    """
    if not _MAC_RE.match(target_mac.strip()):
        return f"ERROR:Invalid MAC '{target_mac}'"
    err = _check_available()
    if err:
        return err
    try:
        devices = _bt.discover_devices(lookup_names=True, duration=8, flush_cache=True)
        for addr, name in devices:
            if addr.upper() == target_mac.upper():
                return f"FOUND:{name}:{addr}"
        return "NOT_FOUND"
    except Exception as e:
        return f"ERROR:{e}"


def pick() -> str:
    """
    Discover any nearby named Bluetooth device (for registration).
    Returns: FOUND\t<name>\t<mac>  |  NOT_FOUND  |  ERROR:<msg>
    """
    err = _check_available()
    if err:
        return err
    try:
        devices = _bt.discover_devices(lookup_names=True, duration=8, flush_cache=True)
        if not devices:
            return "NOT_FOUND"
        for addr, name in devices:
            if name and str(name).strip():
                display = name.replace("\t", " ").replace("\n", " ").strip()
                return f"FOUND\t{display}\t{addr}"
        addr, name = devices[0]
        return f"FOUND\t{name or 'Unknown'}\t{addr}"
    except Exception as e:
        return f"ERROR:{e}"
