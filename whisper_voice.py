"""
WhisperShroom - Voice-to-Text Tool for Windows 11
"""

import os
import sys
import json
import wave
import threading
import time
import tempfile
from pathlib import Path

import sounddevice as sd
import numpy as np
from openai import OpenAI
import pystray
from PIL import Image
import tkinter as tk
from tkinter import messagebox
import ctypes
import ctypes.wintypes

# ==================== WIN32 API (x64-safe type declarations) ====================
# Without explicit argtypes/restype, ctypes defaults to c_int (32-bit) for all
# parameters and return values — this silently truncates handles and pointers on x64.
# See: https://docs.python.org/3/library/ctypes.html#specifying-the-required-argument-types

user32 = ctypes.windll.user32
kernel32 = ctypes.windll.kernel32
shell32 = ctypes.windll.shell32
dwmapi = ctypes.windll.dwmapi

# --- DPI Awareness (Win10 1703+ / Win11) ---
# https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setprocessdpiawarenesscontext
DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = ctypes.c_ssize_t(-4)
user32.SetProcessDpiAwarenessContext.argtypes = [ctypes.c_ssize_t]
user32.SetProcessDpiAwarenessContext.restype = ctypes.wintypes.BOOL

try:
    user32.SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)
except (AttributeError, OSError):
    pass

# --- AppUserModelID ---
# https://learn.microsoft.com/en-us/windows/win32/shell/appids
shell32.SetCurrentProcessExplicitAppUserModelID.argtypes = [ctypes.c_wchar_p]
shell32.SetCurrentProcessExplicitAppUserModelID.restype = ctypes.HRESULT
shell32.SetCurrentProcessExplicitAppUserModelID('shroomlife.whispershroom')

# --- Hotkey API ---
# https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey
user32.RegisterHotKey.argtypes = [ctypes.wintypes.HWND, ctypes.c_int,
                                  ctypes.wintypes.UINT, ctypes.wintypes.UINT]
user32.RegisterHotKey.restype = ctypes.wintypes.BOOL

user32.UnregisterHotKey.argtypes = [ctypes.wintypes.HWND, ctypes.c_int]
user32.UnregisterHotKey.restype = ctypes.wintypes.BOOL

# --- Message Loop ---
user32.GetMessageW.argtypes = [ctypes.POINTER(ctypes.wintypes.MSG),
                               ctypes.wintypes.HWND,
                               ctypes.wintypes.UINT, ctypes.wintypes.UINT]
user32.GetMessageW.restype = ctypes.wintypes.BOOL

user32.PostThreadMessageW.argtypes = [ctypes.wintypes.DWORD, ctypes.wintypes.UINT,
                                      ctypes.wintypes.WPARAM, ctypes.wintypes.LPARAM]
user32.PostThreadMessageW.restype = ctypes.wintypes.BOOL

kernel32.GetCurrentThreadId.argtypes = []
kernel32.GetCurrentThreadId.restype = ctypes.wintypes.DWORD

# --- DWM Window Attributes (Win11 visual features) ---
# https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmsetwindowattribute
dwmapi.DwmSetWindowAttribute.argtypes = [ctypes.wintypes.HWND, ctypes.wintypes.DWORD,
                                         ctypes.c_void_p, ctypes.wintypes.DWORD]
dwmapi.DwmSetWindowAttribute.restype = ctypes.c_long  # HRESULT

# Win32 constants
MOD_ALT = 0x0001
MOD_CTRL = 0x0002
MOD_SHIFT = 0x0004
MOD_WIN = 0x0008
MOD_NOREPEAT = 0x4000
WM_HOTKEY = 0x0312
WM_QUIT = 0x0012
HOTKEY_ID = 1
DWMWA_WINDOW_CORNER_PREFERENCE = 33
DWMWCP_ROUND = 2


def apply_win11_style(hwnd):
    """Apply native Win11 rounded corners to a window via DWM."""
    preference = ctypes.wintypes.DWORD(DWMWCP_ROUND)
    dwmapi.DwmSetWindowAttribute(
        hwnd, DWMWA_WINDOW_CORNER_PREFERENCE,
        ctypes.byref(preference), ctypes.sizeof(preference)
    )

# Map modifier names to flags
_MOD_MAP = {
    'ctrl': MOD_CTRL, 'control': MOD_CTRL,
    'alt': MOD_ALT, 'menu': MOD_ALT,
    'shift': MOD_SHIFT,
    'win': MOD_WIN, 'windows': MOD_WIN,
}

# Map key names to Windows virtual key codes
_VK_MAP = {
    'space': 0x20, 'enter': 0x0D, 'return': 0x0D, 'tab': 0x09,
    'escape': 0x1B, 'esc': 0x1B, 'backspace': 0x08,
    'delete': 0x2E, 'insert': 0x2D, 'home': 0x24, 'end': 0x23,
    'pageup': 0x21, 'pagedown': 0x22, 'page up': 0x21, 'page down': 0x22,
    'up': 0x26, 'down': 0x28, 'left': 0x25, 'right': 0x27,
    'f1': 0x70, 'f2': 0x71, 'f3': 0x72, 'f4': 0x73,
    'f5': 0x74, 'f6': 0x75, 'f7': 0x76, 'f8': 0x77,
    'f9': 0x78, 'f10': 0x79, 'f11': 0x7A, 'f12': 0x7B,
    'numpad0': 0x60, 'numpad1': 0x61, 'numpad2': 0x62, 'numpad3': 0x63,
    'numpad4': 0x64, 'numpad5': 0x65, 'numpad6': 0x66, 'numpad7': 0x67,
    'numpad8': 0x68, 'numpad9': 0x69,
}


def parse_hotkey(hotkey_str):
    """Parse a hotkey string like 'ctrl+shift+r' into (modifiers, vk_code)."""
    parts = [p.strip().lower() for p in hotkey_str.split('+')]
    modifiers = MOD_NOREPEAT  # Prevent repeated firing when held
    vk_code = 0

    for part in parts:
        if part in _MOD_MAP:
            modifiers |= _MOD_MAP[part]
        elif part in _VK_MAP:
            vk_code = _VK_MAP[part]
        elif len(part) == 1 and part.isalnum():
            # Single letter/digit -> use its uppercase ASCII as VK code
            vk_code = ord(part.upper())
        else:
            raise ValueError(f"Unbekannte Taste: '{part}'")

    if vk_code == 0:
        raise ValueError("Keine Taste angegeben (nur Modifier)")
    return modifiers, vk_code


class NativeHotkey:
    """Registers a global hotkey using the Windows RegisterHotKey API."""

    def __init__(self, hotkey_str, callback):
        self.callback = callback
        self._thread = None
        self._thread_id = None
        self._registered = False
        self.hotkey_str = hotkey_str
        self.modifiers, self.vk_code = parse_hotkey(hotkey_str)

    def start(self):
        """Register the hotkey and start listening in a background thread."""
        self._thread = threading.Thread(target=self._listen, daemon=True)
        self._thread.start()

    def _listen(self):
        self._thread_id = kernel32.GetCurrentThreadId()
        if not user32.RegisterHotKey(None, HOTKEY_ID, self.modifiers, self.vk_code):
            self._registered = False
            return
        self._registered = True
        msg = ctypes.wintypes.MSG()
        while user32.GetMessageW(ctypes.byref(msg), None, 0, 0) != 0:
            if msg.message == WM_HOTKEY and msg.wParam == HOTKEY_ID:
                self.callback()
        user32.UnregisterHotKey(None, HOTKEY_ID)
        self._registered = False

    def stop(self):
        """Unregister the hotkey and stop the listener thread."""
        if self._thread_id:
            user32.PostThreadMessageW(self._thread_id, WM_QUIT, 0, 0)
            self._thread_id = None
        if self._thread:
            self._thread.join(timeout=2)
            self._thread = None

CONFIG_PATH = Path(os.environ.get('APPDATA', '.')) / 'WhisperShroom' / 'config.json'

def resource_path(relative_path):
    """Get absolute path to resource, works for dev and for PyInstaller bundle."""
    if hasattr(sys, '_MEIPASS'):
        # Running as bundled EXE
        return os.path.join(sys._MEIPASS, relative_path)
    # Running as script
    return os.path.join(os.path.dirname(os.path.abspath(__file__)), relative_path)

# Window size — reasonable default; DPI awareness handles scaling natively
WIN_W = 560
WIN_H = 420


class WhisperShroom:
    def __init__(self):
        self.api_key = None
        self.client = None
        self.recording = False
        self.audio_data = []
        self.sample_rate = 16000
        self.start_time = None
        self.tray_icon = None
        self.stream = None
        self.hotkey = 'ctrl+shift+e'
        self.device_index = None  # None = system default
        self.device_name = None   # Stored in config for robustness
        self.main_window = None
        self.loading_angle = 0
        self._native_hotkey = None

        self.root = tk.Tk()
        self.root.withdraw()
        self.load_config()
    
    def load_config(self):
        if CONFIG_PATH.exists():
            try:
                with open(CONFIG_PATH, 'r', encoding='utf-8') as f:
                    config = json.load(f)
                    self.api_key = config.get('api_key')
                    self.hotkey = config.get('hotkey', 'ctrl+shift+r')
                    self.device_name = config.get('device_name')
                    self.device_index = self._resolve_device_index(self.device_name)
                    if self.api_key:
                        self.client = OpenAI(api_key=self.api_key)
            except (OSError, json.JSONDecodeError, ValueError):
                pass
    
    def _resolve_device_index(self, name):
        """Resolve a device name to its current PortAudio index, or None."""
        if not name:
            return None
        try:
            for idx, dev_name in self._get_input_devices():
                if dev_name == name:
                    return idx
        except Exception:
            pass
        return None

    def save_config(self):
        CONFIG_PATH.parent.mkdir(parents=True, exist_ok=True)
        with open(CONFIG_PATH, 'w', encoding='utf-8') as f:
            json.dump({
                'api_key': self.api_key,
                'hotkey': self.hotkey,
                'device_name': self.device_name,
            }, f)
    
    def center(self, win):
        win.update_idletasks()
        x = (win.winfo_screenwidth() - WIN_W) // 2
        y = (win.winfo_screenheight() - WIN_H) // 2
        win.geometry(f"{WIN_W}x{WIN_H}+{x}+{y}")
    
    def make_button(self, parent, text, cmd, color='blue'):
        colors = {
            'blue': ('#1a73e8', '#1557b0', 'white'),
            'red': ('#dc3545', '#c82333', 'white'),
            'gray': ('#e0e0e0', '#c0c0c0', '#333'),
            'green': ('#28a745', '#1e7e34', 'white'),
        }
        bg, hover, fg = colors.get(color, colors['blue'])
        
        btn = tk.Button(parent, text=text, command=cmd,
                       font=('Segoe UI', 11, 'bold'),
                       bg=bg, fg=fg, activebackground=hover, activeforeground=fg,
                       relief='flat', bd=0, padx=20, pady=8, cursor='hand2')
        btn.bind('<Enter>', lambda e: btn.config(bg=hover))
        btn.bind('<Leave>', lambda e: btn.config(bg=bg))
        return btn
    
    # ==================== SETUP DIALOG ====================
    def _get_input_devices(self):
        """Return list of (device_index, device_name) for physical input devices.

        Filters to Windows WASAPI host API only, which matches the devices
        shown in Windows Sound Settings (1:1 mapping to physical endpoints).
        Falls back to all input devices if WASAPI is not available.
        """
        devices = sd.query_devices()
        hostapis = sd.query_hostapis()

        # Find the WASAPI host API index
        wasapi_idx = None
        for i, api in enumerate(hostapis):
            if 'WASAPI' in api['name']:
                wasapi_idx = i
                break

        inputs = []
        for i, d in enumerate(devices):
            if d['max_input_channels'] <= 0:
                continue
            # If WASAPI found, only include WASAPI devices
            if wasapi_idx is not None and d['hostapi'] != wasapi_idx:
                continue
            inputs.append((i, d['name']))
        return inputs

    def show_setup_dialog(self):
        dlg = tk.Toplevel(self.root)
        dlg.title("WhisperShroom - Einstellungen")
        dlg.geometry(f"{WIN_W}x{WIN_H + 80}")
        dlg.resizable(False, False)
        dlg.configure(bg='white')
        dlg.attributes('-topmost', True)
        dlg.grab_set()
        try:
            dlg.iconbitmap(resource_path('icon.ico'))
        except tk.TclError:
            pass
        self.center(dlg)
        apply_win11_style(dlg.winfo_id())

        result = {'ok': False}

        # Content frame - centered
        content = tk.Frame(dlg, bg='white')
        content.place(relx=0.5, rely=0.5, anchor='center')

        # Title
        tk.Label(content, text="🎤 WhisperShroom",
                font=('Segoe UI', 20, 'bold'), bg='white', fg='#333').pack(pady=(0, 5))
        tk.Label(content, text="Voice-to-Text mit OpenAI Whisper",
                font=('Segoe UI', 10), bg='white', fg='#666').pack(pady=(0, 20))

        # API Key
        tk.Label(content, text="OpenAI API Key:",
                font=('Segoe UI', 10, 'bold'), bg='white', fg='#333').pack(anchor='w')
        api_var = tk.StringVar(value=self.api_key or '')
        api_entry = tk.Entry(content, textvariable=api_var, font=('Segoe UI', 11),
                            show='•', width=50, relief='solid', bd=1)
        api_entry.pack(pady=(5, 15), ipady=6)

        # Mikrofon
        input_devices = self._get_input_devices()
        device_names = ["Standard-Gerät"] + [d[1] for d in input_devices]
        device_indices = [None] + [d[0] for d in input_devices]

        tk.Label(content, text="Mikrofon:",
                font=('Segoe UI', 10, 'bold'), bg='white', fg='#333').pack(anchor='w')

        # Find current selection
        current_device_name = "Standard-Gerät"
        if self.device_name is not None:
            for idx, name in input_devices:
                if name == self.device_name:
                    current_device_name = name
                    break

        from tkinter import ttk
        device_var = tk.StringVar(value=current_device_name)
        device_combo = ttk.Combobox(content, textvariable=device_var,
                                     values=device_names, state='readonly',
                                     font=('Segoe UI', 10), width=48)
        device_combo.pack(pady=(5, 15), ipady=4)

        # Hotkey
        tk.Label(content, text="Tastenkombination:",
                font=('Segoe UI', 10, 'bold'), bg='white', fg='#333').pack(anchor='w')
        tk.Label(content, text="(z.B. ctrl+shift+r, alt+r, ctrl+space)",
                font=('Segoe UI', 9), bg='white', fg='#888').pack(anchor='w')
        hotkey_var = tk.StringVar(value=self.hotkey)
        hotkey_entry = tk.Entry(content, textvariable=hotkey_var, font=('Segoe UI', 11),
                               width=50, relief='solid', bd=1)
        hotkey_entry.pack(pady=(5, 25), ipady=6)

        # Buttons
        btn_frame = tk.Frame(content, bg='white')
        btn_frame.pack()

        def save():
            key = api_var.get().strip()
            hk = hotkey_var.get().strip().lower() or 'ctrl+shift+r'
            if not key:
                messagebox.showerror("Fehler", "API-Key wird benötigt!")
                return
            self.api_key = key
            self.hotkey = hk
            # Resolve selected device
            selected_name = device_var.get()
            if selected_name == "Standard-Gerät":
                self.device_name = None
                self.device_index = None
            else:
                self.device_name = selected_name
                self.device_index = self._resolve_device_index(selected_name)
            self.client = OpenAI(api_key=self.api_key)
            self.save_config()
            result['ok'] = True
            dlg.destroy()

        self.make_button(btn_frame, "Speichern", save, 'green').pack(side='left', padx=(0, 15))
        self.make_button(btn_frame, "Abbrechen", dlg.destroy, 'gray').pack(side='left')

        api_entry.focus_set()
        dlg.wait_window()
        return result['ok']
    
    # ==================== TRAY ICON ====================
    def create_tray_icon(self):
        return Image.open(resource_path("icon.ico"))

    def update_tray(self):
        if self.tray_icon:
            self.tray_icon.icon = self.create_tray_icon()
    
    # ==================== MAIN WINDOW ====================
    def show_main_window(self):
        if self.main_window and self.main_window.winfo_exists():
            self.main_window.lift()
            return
        
        self.main_window = tk.Toplevel(self.root)
        self.main_window.title("WhisperShroom")
        self.main_window.geometry(f"{WIN_W}x{WIN_H}")
        self.main_window.resizable(False, False)
        self.main_window.configure(bg='white')
        self.main_window.attributes('-topmost', True)
        try:
            self.main_window.iconbitmap(resource_path('icon.ico'))
        except tk.TclError:
            pass
        self.center(self.main_window)
        apply_win11_style(self.main_window.winfo_id())
        
        # Container for content - centered
        self.container = tk.Frame(self.main_window, bg='white')
        self.container.place(relx=0.5, rely=0.5, anchor='center', width=WIN_W-100, height=WIN_H-100)
        
        self.show_ready_state()
        self.main_window.protocol("WM_DELETE_WINDOW", self.close_main_window)
    
    def close_main_window(self):
        # Cancel recording without transcribing
        if self.recording:
            self.recording = False
            try:
                if self.stream:
                    self.stream.stop()
                    self.stream.close()
            except Exception:
                pass
            finally:
                self.stream = None
            self.audio_data = []  # Discard audio
            self.update_tray()
        
        # Mark window as closed immediately so threads stop processing
        mw = self.main_window
        self.main_window = None
        if mw:
            try:
                mw.destroy()
            except Exception:
                pass
    
    def clear_container(self):
        if not self.main_window or not self.container.winfo_exists():
            return
        for w in self.container.winfo_children():
            w.destroy()
    
    # ==================== STATES ====================
    def show_ready_state(self):
        self.clear_container()
        
        inner = tk.Frame(self.container, bg='white')
        inner.place(relx=0.5, rely=0.5, anchor='center')
        
        tk.Label(inner, text="🎤", font=('Segoe UI', 48), bg='white').pack(pady=(0, 15))
        tk.Label(inner, text="Bereit zur Aufnahme",
                font=('Segoe UI', 18, 'bold'), bg='white', fg='#333').pack(pady=(0, 8))
        tk.Label(inner, text=f"Hotkey: {self.hotkey.upper()}",
                font=('Segoe UI', 10), bg='white', fg='#888').pack(pady=(0, 25))
        self.make_button(inner, "Aufnahme starten", self.start_recording, 'blue').pack()
    
    def show_recording_state(self):
        self.clear_container()
        
        inner = tk.Frame(self.container, bg='white')
        inner.place(relx=0.5, rely=0.5, anchor='center')
        
        self.rec_dot = tk.Label(inner, text="⏺", font=('Segoe UI', 36), bg='white', fg='#e53935')
        self.rec_dot.pack(pady=(0, 10))
        
        tk.Label(inner, text="Aufnahme läuft...",
                font=('Segoe UI', 18, 'bold'), bg='white', fg='#333').pack(pady=(0, 15))
        
        self.time_label = tk.Label(inner, text="00:00",
                                   font=('Segoe UI', 32, 'bold'), bg='white', fg='#333')
        self.time_label.pack(pady=(0, 25))
        
        # Button frame for Stop and Cancel
        btn_frame = tk.Frame(inner, bg='white')
        btn_frame.pack()
        
        self.make_button(btn_frame, "Stoppen", self.stop_recording, 'red').pack(side='left', padx=(0, 10))
        self.make_button(btn_frame, "Abbrechen", self.close_main_window, 'gray').pack(side='left')

        # Silence warning (hidden initially)
        self.silence_label = tk.Label(inner, text="Kein Audiosignal erkannt.\nBitte Mikrofon prüfen.",
                                      font=('Segoe UI', 9), bg='white', fg='#e53935')

        self.update_timer()
        self.pulse_dot()
        self.check_silence()
    
    def show_loading_state(self):
        self.clear_container()
        
        inner = tk.Frame(self.container, bg='white')
        inner.place(relx=0.5, rely=0.5, anchor='center')
        
        self.spinner = tk.Canvas(inner, width=60, height=60, bg='white', highlightthickness=0)
        self.spinner.pack(pady=(0, 15))
        
        tk.Label(inner, text="Wird transkribiert...",
                font=('Segoe UI', 14, 'bold'), bg='white', fg='#333').pack()
        
        self.loading_angle = 0
        self.animate_spinner()
    
    def animate_spinner(self):
        if not self.main_window or not self.main_window.winfo_exists():
            return
        if not hasattr(self, 'spinner') or not self.spinner.winfo_exists():
            return
        self.spinner.delete('all')
        self.loading_angle = (self.loading_angle + 10) % 360
        self.spinner.create_arc(5, 5, 55, 55, start=self.loading_angle,
                               extent=270, style='arc', width=5, outline='#1a73e8')
        self.main_window.after(30, self.animate_spinner)
    
    def show_result_state(self, text):
        self.clear_container()
        
        # Header
        header = tk.Frame(self.container, bg='white')
        header.pack(fill='x', pady=(20, 15))
        tk.Label(header, text="✅", font=('Segoe UI', 20), bg='white').pack(side='left', padx=(0, 10))
        tk.Label(header, text="Transkription abgeschlossen",
                font=('Segoe UI', 14, 'bold'), bg='white', fg='#333').pack(side='left')
        
        # --- Bottom Buttons (Grid Layout for equal width) ---
        btn_frame = tk.Frame(self.container, bg='white')
        btn_frame.pack(side='bottom', fill='x', pady=20)
        
        # Configure 2 equal columns
        btn_frame.grid_columnconfigure(0, weight=1)
        btn_frame.grid_columnconfigure(1, weight=1)
        
        # New Recording
        self.make_button(btn_frame, "Neue Aufnahme", self.show_ready_state, 'blue').grid(
            row=0, column=0, padx=(0, 10), sticky='ew')
            
        # Copy Button (silent copy, no text change)
        def copy():
            self.main_window.clipboard_clear()
            self.main_window.clipboard_append(text)
        
        copy_btn = self.make_button(btn_frame, "Text kopieren", copy, 'green')
        copy_btn.grid(row=0, column=1, padx=(10, 0), sticky='ew')
        
        # --- Text Area (Fills remaining space) ---
        text_frame = tk.Frame(self.container, bg='#ddd')
        text_frame.pack(fill='both', expand=True, pady=(0, 15))
        
        scrollbar = tk.Scrollbar(text_frame)
        scrollbar.pack(side='right', fill='y')
        
        self.result_text = tk.Text(text_frame, wrap='word', font=('Segoe UI', 11),
                                   bg='#f9f9f9', fg='#333', relief='flat',
                                   padx=10, pady=10, yscrollcommand=scrollbar.set)
        self.result_text.pack(side='left', fill='both', expand=True)
        self.result_text.insert('1.0', text)
        scrollbar.config(command=self.result_text.yview)
    
    def show_error_state(self, msg):
        self.clear_container()
        
        inner = tk.Frame(self.container, bg='white')
        inner.place(relx=0.5, rely=0.5, anchor='center')
        
        tk.Label(inner, text="⚠️", font=('Segoe UI', 36), bg='white').pack(pady=(0, 10))
        tk.Label(inner, text="Fehler", font=('Segoe UI', 14, 'bold'), bg='white', fg='#e53935').pack(pady=(0, 8))
        tk.Label(inner, text=msg[:150], font=('Segoe UI', 10), bg='white', fg='#666',
                wraplength=400).pack(pady=(0, 20))
        self.make_button(inner, "🔄 Nochmal", self.show_ready_state, 'blue').pack()
    
    # ==================== RECORDING ====================
    def audio_callback(self, indata, frames, time_info, status):
        if self.recording:
            self.audio_data.append(indata.copy())
            # Track whether we've seen any real audio
            rms = float(np.sqrt(np.mean(indata ** 2)))
            if rms > 0.005:
                self._has_audio = True
    
    def start_recording(self):
        if self.recording:
            return
        try:
            self.recording = True
            self.audio_data = []
            self._has_audio = False
            self._silence_warned = False
            self.start_time = time.monotonic()
            self.update_tray()
            self.stream = sd.InputStream(samplerate=self.sample_rate, channels=1,
                                         dtype='float32', callback=self.audio_callback,
                                         device=self.device_index)
            self.stream.start()
            self.root.after(0, self._show_recording_window)
        except Exception as e:
            self.recording = False
            self.update_tray()
            self.root.after(0, lambda: self.show_error_state(str(e)))
    
    def stop_recording(self):
        if not self.recording:
            return
        self.recording = False
        if self.stream:
            self.stream.stop()
            self.stream.close()
            self.stream = None
        if self.audio_data:
            self.update_tray()
            self.root.after(0, self.show_loading_state)
            threading.Thread(target=self.transcribe, daemon=True).start()
        else:
            self.update_tray()
            self.root.after(0, self.show_ready_state)
    
    def _show_recording_window(self):
        """Show main window and immediately transition to recording state."""
        self.show_main_window()
        self.show_recording_state()

    def toggle_recording(self):
        if self.recording:
            self.root.after(0, self.stop_recording)
        else:
            self.root.after(0, self.start_recording)
    
    def update_timer(self):
        if self.recording and hasattr(self, 'time_label') and self.time_label.winfo_exists():
            try:
                elapsed = int(time.monotonic() - self.start_time)
                m, s = divmod(elapsed, 60)
                self.time_label.config(text=f"{m:02d}:{s:02d}")
                self.main_window.after(200, self.update_timer)
            except tk.TclError:
                pass

    def check_silence(self):
        if not self.recording or not self.main_window:
            return
        if not hasattr(self, 'silence_label') or not self.silence_label.winfo_exists():
            return
        try:
            elapsed = time.monotonic() - self.start_time
            if elapsed >= 5 and not self._has_audio and not self._silence_warned:
                # Show warning
                self._silence_warned = True
                self.silence_label.pack(pady=(10, 0))
            elif self._has_audio and self._silence_warned:
                # Audio detected, hide warning
                self.silence_label.pack_forget()
            self.main_window.after(500, self.check_silence)
        except tk.TclError:
            pass

    def pulse_dot(self):
        if self.recording and hasattr(self, 'rec_dot') and self.rec_dot.winfo_exists():
            try:
                c = self.rec_dot.cget('fg')
                self.rec_dot.config(fg='white' if c == '#e53935' else '#e53935')
                self.main_window.after(500, self.pulse_dot)
            except tk.TclError:
                pass
    
    # Known Whisper hallucination strings (appear on silent/near-silent audio)
    _HALLUCINATIONS = {
        "untertitel der amara.org-community",
        "untertitel von der amara.org-community",
        "untertitel im auftrag des zdf für funk, 2017",
        "untertitel im auftrag des zdf, 2017",
        "untertitel im auftrag des zdf, 2020",
        "swr 2020",
        "swr 2021",
        "copyright wdr 2020",
        "copyright wdr 2021",
        "vielen dank fürs zuschauen!",
        "vielen dank für's zuschauen!",
        "bis zum nächsten mal!",
        "danke fürs zuschauen!",
        "danke für's zuschauen!",
        "tschüss!",
    }

    def transcribe(self):
        tmp = None
        try:
            audio = np.concatenate(self.audio_data, axis=0)

            # Silence detection — reject audio that's too quiet to contain speech
            rms = float(np.sqrt(np.mean(audio ** 2)))
            if rms < 0.005:
                if self.main_window:
                    self.root.after(0, lambda: self.show_error_state(
                        "Kein Audio erkannt. Bitte prüfe dein Mikrofon "
                        "in den Einstellungen."))
                return

            with tempfile.NamedTemporaryFile(suffix='.wav', delete=False) as f:
                tmp = f.name
            audio16 = (audio * 32767).astype(np.int16)
            with wave.open(tmp, 'wb') as w:
                w.setnchannels(1)
                w.setsampwidth(2)
                w.setframerate(self.sample_rate)
                w.writeframes(audio16.tobytes())
            with open(tmp, 'rb') as f:
                result = self.client.audio.transcriptions.create(
                    model="whisper-1",
                    file=f,
                    language="de"
                )

            # If window was closed while transcribing, do nothing
            if not self.main_window:
                return

            # Filter known Whisper hallucinations
            text = result.text.strip()
            if text.lower().rstrip('.') in self._HALLUCINATIONS:
                self.root.after(0, lambda: self.show_error_state(
                    "Kein Audio erkannt. Bitte prüfe dein Mikrofon "
                    "in den Einstellungen."))
                return

            self.root.after(0, lambda: self.show_result_state(text))
        except Exception as e:
            if self.main_window: # Only show error if window is open
                 self.root.after(0, lambda: self.show_error_state(str(e)))
        finally:
            if tmp:
                try:
                    os.unlink(tmp)
                except OSError:
                    pass
            self.update_tray()
    
    # ==================== SETTINGS / QUIT ====================
    def _register_hotkey(self):
        """Register the global hotkey using the native Windows API."""
        if self._native_hotkey:
            self._native_hotkey.stop()
        self._native_hotkey = NativeHotkey(self.hotkey, self.toggle_recording)
        self._native_hotkey.start()

    def open_settings(self):
        if self.show_setup_dialog():
            self._register_hotkey()
            if self.tray_icon:
                self.tray_icon.title = f"WhisperShroom ({self.hotkey.upper()})"

    def quit_app(self, *args):
        if self.recording:
            self.stop_recording()
        if self._native_hotkey:
            self._native_hotkey.stop()
        if self.tray_icon:
            self.tray_icon.stop()
        self.root.quit()
        self.root.destroy()
    
    # ==================== RUN ====================
    def _build_device_menu(self):
        """Build a submenu of input devices for the tray icon."""
        input_devices = self._get_input_devices()
        items = []

        def make_select(dev_name, dev_idx):
            def select():
                self.device_name = dev_name
                self.device_index = dev_idx
                self.save_config()
                self._rebuild_tray_menu()
            return select

        # "Standard-Gerät" option
        items.append(pystray.MenuItem(
            'Standard-Gerät',
            make_select(None, None),
            checked=lambda item: self.device_name is None,
        ))

        for idx, name in input_devices:
            items.append(pystray.MenuItem(
                name,
                make_select(name, idx),
                checked=lambda item, dn=name: self.device_name == dn,
            ))

        return pystray.Menu(*items)

    def _build_tray_menu(self):
        return pystray.Menu(
            pystray.MenuItem('Aufnahme starten/stoppen', lambda: self.toggle_recording()),
            pystray.MenuItem(f'Hotkey: {self.hotkey.upper()}', lambda: None, enabled=False),
            pystray.Menu.SEPARATOR,
            pystray.MenuItem('Mikrofon', self._build_device_menu()),
            pystray.MenuItem('Einstellungen', lambda: self.root.after(0, self.open_settings)),
            pystray.Menu.SEPARATOR,
            pystray.MenuItem('Beenden', self.quit_app),
        )

    def _rebuild_tray_menu(self):
        if self.tray_icon:
            self.tray_icon.menu = self._build_tray_menu()

    def run(self):
        if not self.api_key:
            if not self.show_setup_dialog():
                return

        self._register_hotkey()

        self.tray_icon = pystray.Icon("WhisperShroom", self.create_tray_icon(),
                                      f"WhisperShroom ({self.hotkey.upper()})",
                                      self._build_tray_menu())
        threading.Thread(target=self.tray_icon.run, daemon=True).start()
        self.root.mainloop()


if __name__ == "__main__":
    app = WhisperShroom()
    app.run()
