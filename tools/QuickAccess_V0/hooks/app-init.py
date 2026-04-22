# -*- coding: utf-8 -*-
import os
import sys

# Get the extension directory
extension_dir = os.path.dirname(os.path.dirname(__file__))
lib_path = os.path.join(extension_dir, "lib")

if lib_path not in sys.path:
    sys.path.append(lib_path)

try:
    import quick_access
    # Update ribbon titles at startup/reload
    quick_access.update_ribbon_titles()
except Exception as e:
    # Fail silently to avoid blocking Revit startup
    pass
