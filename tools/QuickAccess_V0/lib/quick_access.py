# -*- coding: utf-8 -*-
import os
import json
from pyrevit import script

# Path to the config file
CONFIG_PATH = os.path.join(os.path.dirname(__file__), "quick_access_config.json")

def get_config():
    """Read the configuration from JSON file."""
    if os.path.exists(CONFIG_PATH):
        try:
            with open(CONFIG_PATH, "r") as f:
                return json.load(f)
        except Exception:
            pass
    
    # Default values if config doesn't exist or fails
    return {
        "pipe_types": [None, None, None, None, None]
    }

def save_config(config):
    """Save the configuration to JSON file."""
    try:
        with open(CONFIG_PATH, "w") as f:
            json.dump(config, f, indent=4)
        return True
    except Exception as e:
        print("Error saving config: {}".format(e))
        return False

def get_pipe_type_name(index):
    """Get the name of the pipe type at the given index (0-4)."""
    config = get_config()
    types = config.get("pipe_types", [None] * 5)
    if index < len(types) and index >= 0:
        return types[index]
    return None

def update_ribbon_titles():
    """Update the ribbon button titles based on current configuration."""
    config = get_config()
    types = config.get("pipe_types", [None] * 5)
    
    for i, name in enumerate(types):
        if name:
            try:
                # Attempt to find the pushbutton and update its title
                # In pyRevit, we can use script.get_button()
                btn_id = "Pipe{}".format(i + 1)
                button = script.get_button(btn_id)
                if button:
                    button.title = name
            except Exception:
                pass
