# -*- coding: utf-8 -*-
"""Settings for MCP Marking."""

__title__ = "Mark Settings"
__author__ = "MCP"

import os
from pyrevit import forms, script


class MCPSettingsWindow(forms.WPFWindow):
    def __init__(self, xaml_file, prefix, padding, index, suffix, show_dialog):
        forms.WPFWindow.__init__(self, xaml_file)
        self.prefix_box.Text = str(prefix or "MAU-")
        self.padding_box.Text = str(padding)
        self.index_box.Text = str(index)
        self.suffix_box.Text = "" if suffix is None else str(suffix)
        self.dialog_checkbox.IsChecked = bool(show_dialog)
        self.response = None

    def on_save(self, sender, args):
        prefix_text = self.prefix_box.Text or "MAU-"
        suffix_text = self.suffix_box.Text

        try:
            padding_value = int(self.padding_box.Text)
            index_value = int(self.index_box.Text)
        except ValueError:
            forms.alert("Padding and Start Index must be integers.", title="Mark Settings")
            return

        if padding_value < 1:
            forms.alert("Padding must be at least 1. Value 1 means no zero fill.", title="Mark Settings")
            return

        if index_value < 0:
            forms.alert("Start Index can not be less than 0.", title="Mark Settings")
            return

        self.response = {
            "prefix": prefix_text,
            "padding": padding_value,
            "index": index_value,
            "suffix": "" if suffix_text is None else suffix_text,
            "show_dialog": bool(self.dialog_checkbox.IsChecked)
        }
        self.Close()


def main():
    config = script.get_config("MCP_Marking")
    curr_prefix = config.get_option("prefix", "MAU-")
    if not curr_prefix:
        curr_prefix = "MAU-"
    curr_padding = config.get_option("padding", 4)
    curr_index = config.get_option("index", 1)
    curr_suffix = config.get_option("suffix", "")
    curr_show_dialog = config.get_option("show_dialog", True)

    xaml_file = os.path.join(os.path.dirname(__file__), "ui.xaml")
    window = MCPSettingsWindow(
        xaml_file,
        curr_prefix,
        curr_padding,
        curr_index,
        curr_suffix,
        curr_show_dialog,
    )
    window.show_dialog()

    if window.response:
        try:
            config.set_option("prefix", window.response["prefix"])
            config.set_option("padding", window.response["padding"])
            config.set_option("index", window.response["index"])
            config.set_option("suffix", window.response["suffix"])
            config.set_option("show_dialog", window.response["show_dialog"])
            script.save_config()
            forms.toast("Settings saved.", title="MCP Settings")
        except Exception as ex:
            forms.alert("Failed to save settings: {}".format(ex), title="Mark Settings")


if __name__ == "__main__":
    main()