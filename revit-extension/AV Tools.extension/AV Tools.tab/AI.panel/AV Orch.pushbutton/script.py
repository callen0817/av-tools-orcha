# -*- coding: utf-8 -*-
"""Launch AV Orch from the shared AV Tools Revit ribbon tab."""
__title__ = "AV Orch"
__author__ = "Artificial Vision, Inc."

import os
import subprocess
from pyrevit import forms


def find_av_orch_exe():
    local_app_data = os.environ.get("LOCALAPPDATA", "")
    program_files = os.environ.get("ProgramFiles", "")
    candidates = [
        os.path.join(local_app_data, "Artificial Vision", "AV Orch", "AVOrch.exe"),
        os.path.join(program_files, "Artificial Vision", "AV Orch", "AVOrch.exe"),
    ]
    for path in candidates:
        if path and os.path.isfile(path):
            return path
    return None


def main():
    exe_path = find_av_orch_exe()
    if not exe_path:
        forms.alert(
            "AV Orch desktop application (AVOrch.exe) was not found.\n\n"
            "Please install or repair AV Orch using the AV Orch installer.",
            title="AV Orch — Not Found",
            warn_icon=True,
        )
        return

    try:
        subprocess.Popen([exe_path], cwd=os.path.dirname(exe_path))
    except Exception as exc:
        forms.alert(
            "Could not launch AV Orch:\n{0}".format(exc),
            title="AV Orch — Launch Error",
            warn_icon=True,
        )


if __name__ == "__main__":
    main()
