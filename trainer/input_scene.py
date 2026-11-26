"""Publish the local player's current scene to the input controller."""
import ctypes
from ctypes import wintypes
import hashlib
import os
from pathlib import Path


class InputScene:
    def __init__(self, root):
        path = os.path.normcase(str(Path(root).resolve())).rstrip('\\')
        identifier = hashlib.sha256(path.encode('utf-8')).hexdigest()[:24]
        self.prefix = 'Local\\AradHomeward.Input.' + identifier + '.'
        self.handle = None
        self.scene = ''

    def set(self, scene):
        if scene == self.scene:
            return
        self.scene = scene
        if os.name != 'nt':
            return
        kernel = ctypes.windll.kernel32
        kernel.CreateEventW.argtypes = [ctypes.c_void_p, wintypes.BOOL, wintypes.BOOL, wintypes.LPCWSTR]
        kernel.CreateEventW.restype = wintypes.HANDLE
        kernel.CloseHandle.argtypes = [wintypes.HANDLE]
        if self.handle:
            kernel.CloseHandle(self.handle)
            self.handle = None
        if scene in ('town', 'dungeon'):
            self.handle = kernel.CreateEventW(None, True, True, self.prefix + scene)
