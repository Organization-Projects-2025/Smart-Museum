"""
Check gesture templates file
"""
import pickle
import os
from datetime import datetime

templates_file = "gesture_templates.pkl"

if not os.path.exists(templates_file):
    print(f"✗ Templates file not found: {templates_file}")
    exit(1)

# Get file info
file_stat = os.stat(templates_file)
file_size = file_stat.st_size
file_mtime = datetime.fromtimestamp(file_stat.st_mtime)

print(f"Templates file: {os.path.abspath(templates_file)}")
print(f"Size: {file_size:,} bytes")
print(f"Last modified: {file_mtime}")
print()

# Load and analyze templates
try:
    with open(templates_file, 'rb') as f:
        templates = pickle.load(f)
    
    print(f"✓ Loaded {len(templates)} templates")
    print()
    
    # Count by gesture name
    gesture_counts = {}
    for template in templates:
        name = template.name
        gesture_counts[name] = gesture_counts.get(name, 0) + 1
    
    print("Templates by gesture:")
    for name in sorted(gesture_counts.keys()):
        count = gesture_counts[name]
        print(f"  {name}: {count} template(s)")
    
    print()
    print("✓ Templates are valid and ready to use")
    
except Exception as e:
    print(f"✗ Error loading templates: {e}")
    import traceback
    traceback.print_exc()
