# pip install pybluez2
import csv
import os
import sys

import bluetooth


def save_mac_to_users_csv(csv_path, user_id, mac_address):
    if not os.path.exists(csv_path):
        raise RuntimeError("users.csv not found: " + csv_path)

    with open(csv_path, "r", newline="", encoding="utf-8") as f:
        reader = csv.DictReader(f)
        fieldnames = list(reader.fieldnames or [])
        rows = list(reader)

    if "face_user_id" not in fieldnames or "preferred_bluetooth_name" not in fieldnames:
        raise RuntimeError("users.csv must contain face_user_id and preferred_bluetooth_name columns")

    found = False
    for row in rows:
        if (row.get("face_user_id") or "").strip().lower() == user_id.strip().lower():
            row["preferred_bluetooth_name"] = mac_address
            found = True
            break

    if not found:
        raise RuntimeError("user id not found in users.csv: " + user_id)

    with open(csv_path, "w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(rows)


def main():
    default_csv = os.path.join(os.getcwd(), "C#", "content", "auth", "users.csv")

    user_id = ""
    duration = 8
    csv_path = default_csv
    device_index = -1

    args = sys.argv[1:]
    i = 0
    while i < len(args):
        if args[i] == "--user-id" and i + 1 < len(args):
            user_id = args[i + 1].strip()
            i += 2
            continue
        if args[i] == "--duration" and i + 1 < len(args):
            try:
                duration = max(1, int(args[i + 1]))
            except ValueError:
                duration = 8
            i += 2
            continue
        if args[i] == "--save-file" and i + 1 < len(args):
            csv_path = args[i + 1]
            i += 2
            continue
        if args[i] == "--device-index" and i + 1 < len(args):
            try:
                device_index = int(args[i + 1])
            except ValueError:
                device_index = -1
            i += 2
            continue
        i += 1

    if not user_id:
        user_id = input("Enter user id to save Bluetooth MAC for: ").strip()

    if not user_id:
        print("ERROR: user id is required")
        print("RESULT_STATUS:ERROR")
        return

    print("Scanning for bluetooth devices...")
    devices = bluetooth.discover_devices(lookup_names=True, duration=duration, flush_cache=True)

    if not devices:
        print("No Bluetooth devices found.")
        print("RESULT_STATUS:NOT_FOUND")
        return

    print("Select the user phone from the list:")
    for idx, (addr, name) in enumerate(devices, start=1):
        print(f"{idx}. {name or 'Unknown'} - {addr}")

    selected_index = device_index
    while selected_index < 1 or selected_index > len(devices):
        raw = input("Enter number: ").strip()
        try:
            selected_index = int(raw)
        except ValueError:
            selected_index = -1

    selected_addr, selected_name = devices[selected_index - 1]
    selected_name = selected_name or "Unknown"

    save_mac_to_users_csv(csv_path, user_id, selected_addr)

    print("Saved successfully.")
    print("RESULT_STATUS:FOUND")
    print("RESULT_USER_ID:" + user_id)
    print("RESULT_DEVICE_NAME:" + selected_name)
    print("RESULT_MAC:" + selected_addr)
    print("RESULT_SAVED_TO:" + csv_path)


if __name__ == "__main__":
    main()
