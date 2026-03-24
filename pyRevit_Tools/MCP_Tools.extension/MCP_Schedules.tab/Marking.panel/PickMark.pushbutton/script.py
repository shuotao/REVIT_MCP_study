# -*- coding: utf-8 -*-
"""Pick elements and write incrementing Mark values."""

__title__ = "Pick Mark"
__author__ = "MCP"

import clr
import ctypes
import os

clr.AddReference("PresentationCore")
clr.AddReference("PresentationFramework")
clr.AddReference("WindowsBase")

from System.Windows import Point
from Autodesk.Revit import DB
from pyrevit import forms, revit, script


class POINT(ctypes.Structure):
    _fields_ = [("x", ctypes.c_long), ("y", ctypes.c_long)]


def get_mouse_screen_pos():
    pt = POINT()
    ctypes.windll.user32.GetCursorPos(ctypes.byref(pt))
    return pt.x, pt.y


def get_writable_mark_param(element):
    try:
        param = element.get_Parameter(DB.BuiltInParameter.ALL_MODEL_MARK)
        if param and not param.IsReadOnly:
            return param
    except Exception:
        pass

    for param_name in ["Mark", "MARK"]:
        try:
            param = element.LookupParameter(param_name)
            if param and not param.IsReadOnly:
                return param
        except Exception:
            pass

    return None


class MCPSuccessWindow(forms.WPFWindow):
    def __init__(self, xaml_file, mark_text, spawn_x, spawn_y):
        forms.WPFWindow.__init__(self, xaml_file)
        self.msg_text.Text = str(mark_text)
        self.Left = spawn_x + 50
        self.Top = spawn_y + 50
        self.ContentRendered += self.on_rendered

    def on_rendered(self, sender, args):
        try:
            screen_pos = self.ok_btn.PointToScreen(Point(50, 17))
            ctypes.windll.user32.SetCursorPos(int(screen_pos.X), int(screen_pos.Y))
        except Exception:
            pass

    def on_ok(self, sender, args):
        self.Close()


def format_mark(prefix, idx, padding, suffix):
    prefix_text = str(prefix or "MAU-")
    suffix_text = "" if suffix is None else str(suffix)
    padding_value = int(padding)

    if padding_value <= 1:
        number_text = str(idx)
    else:
        fmt = "{:0" + str(padding_value) + "d}"
        number_text = fmt.format(idx)

    return "{}{}{}".format(prefix_text, number_text, suffix_text)


def pick_and_mark():
    config = script.get_config("MCP_Marking")
    prefix = config.get_option("prefix", "MAU-")
    if not prefix:
        prefix = "MAU-"
        config.set_option("prefix", prefix)
        script.save_config()

    padding = config.get_option("padding", 4)
    curr_idx = config.get_option("index", 1)
    suffix = config.get_option("suffix", "")
    show_dialog = config.get_option("show_dialog", True)

    xaml_file = os.path.join(os.path.dirname(__file__), "confirm.xaml")
    count = 0

    while True:
        try:
            element = revit.pick_element("Select an element to write Mark. Press ESC to finish.")
        except Exception:
            break

        if not element:
            break

        mouse_x, mouse_y = get_mouse_screen_pos()
        mark_value = format_mark(prefix, curr_idx, padding, suffix)

        try:
            with revit.Transaction("MCP: Auto Mark {}".format(mark_value)):
                param = get_writable_mark_param(element)
                if not param:
                    forms.alert("This element does not have a writable Mark parameter.", title="Pick Mark")
                    continue
                param.Set(mark_value)
        except Exception as ex:
            forms.alert("Failed to set Mark: {}".format(ex), title="Pick Mark")
            continue

        if show_dialog:
            window = MCPSuccessWindow(xaml_file, mark_value, mouse_x, mouse_y)
            window.show_dialog()

        count += 1
        curr_idx += 1
        config.set_option("index", curr_idx)
        script.save_config()

    forms.alert("Finished. Updated {} element(s).".format(count), title="Pick Mark")


if __name__ == "__main__":
    pick_and_mark()