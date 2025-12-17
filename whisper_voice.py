"""
WhisperShroom - Simple Voice-to-Text Tool for Windows 11
Large window (800x600), compact layout
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
from PIL import Image, ImageDraw
import keyboard
import tkinter as tk
from tkinter import messagebox
import ctypes

CONFIG_PATH = Path(os.environ.get('APPDATA', '.')) / 'WhisperShroom' / 'config.json'

# Window size - LARGE to account for DPI scaling
WIN_W = 800
WIN_H = 600


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
        self.main_window = None
        self.loading_angle = 0
        
        self.root = tk.Tk()
        self.root.withdraw()
        self.load_config()
    
    def load_config(self):
        if CONFIG_PATH.exists():
            try:
                with open(CONFIG_PATH, 'r') as f:
                    config = json.load(f)
                    self.api_key = config.get('api_key')
                    self.hotkey = config.get('hotkey', 'ctrl+shift+r')
                    if self.api_key:
                        self.client = OpenAI(api_key=self.api_key)
            except:
                pass
    
    def save_config(self):
        CONFIG_PATH.parent.mkdir(parents=True, exist_ok=True)
        with open(CONFIG_PATH, 'w') as f:
            json.dump({'api_key': self.api_key, 'hotkey': self.hotkey}, f)
    
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
                       font=('Arial', 11, 'bold'),
                       bg=bg, fg=fg, activebackground=hover, activeforeground=fg,
                       relief='flat', bd=0, padx=20, pady=8, cursor='hand2')
        btn.bind('<Enter>', lambda e: btn.config(bg=hover))
        btn.bind('<Leave>', lambda e: btn.config(bg=bg))
        return btn
    
    # ==================== SETUP DIALOG ====================
    def show_setup_dialog(self):
        dlg = tk.Toplevel(self.root)
        dlg.title("WhisperShroom - Einstellungen")
        dlg.geometry(f"{WIN_W}x{WIN_H}")
        dlg.resizable(False, False)
        dlg.configure(bg='white')
        dlg.attributes('-topmost', True)
        dlg.grab_set()
        try:
            dlg.iconbitmap('icon.ico')
        except:
            pass
        self.center(dlg)
        
        result = {'ok': False}
        
        # Content frame - centered
        content = tk.Frame(dlg, bg='white')
        content.place(relx=0.5, rely=0.5, anchor='center')
        
        # Title
        tk.Label(content, text="🎤 WhisperShroom",
                font=('Arial', 20, 'bold'), bg='white', fg='#333').pack(pady=(0, 5))
        tk.Label(content, text="Voice-to-Text mit OpenAI Whisper",
                font=('Arial', 10), bg='white', fg='#666').pack(pady=(0, 30))
        
        # API Key
        tk.Label(content, text="OpenAI API Key:",
                font=('Arial', 10, 'bold'), bg='white', fg='#333').pack(anchor='w')
        api_var = tk.StringVar(value=self.api_key or '')
        api_entry = tk.Entry(content, textvariable=api_var, font=('Arial', 11),
                            show='•', width=50, relief='solid', bd=1)
        api_entry.pack(pady=(5, 20), ipady=6)
        
        # Hotkey
        tk.Label(content, text="Tastenkombination:",
                font=('Arial', 10, 'bold'), bg='white', fg='#333').pack(anchor='w')
        tk.Label(content, text="(z.B. ctrl+shift+r, alt+r, ctrl+space)",
                font=('Arial', 9), bg='white', fg='#888').pack(anchor='w')
        hotkey_var = tk.StringVar(value=self.hotkey)
        hotkey_entry = tk.Entry(content, textvariable=hotkey_var, font=('Arial', 11),
                               width=50, relief='solid', bd=1)
        hotkey_entry.pack(pady=(5, 30), ipady=6)
        
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
    def create_tray_icon(self, state='idle'):
        return Image.open("icon.ico")
    
    def update_tray(self, state):
        if self.tray_icon:
            self.tray_icon.icon = self.create_tray_icon(state)
    
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
            self.main_window.iconbitmap('icon.ico')
        except:
            pass
        self.center(self.main_window)
        
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
            self.update_tray('idle')
        
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
        
        tk.Label(inner, text="🎤", font=('Arial', 48), bg='white').pack(pady=(0, 15))
        tk.Label(inner, text="Bereit zur Aufnahme",
                font=('Arial', 18, 'bold'), bg='white', fg='#333').pack(pady=(0, 8))
        tk.Label(inner, text=f"Hotkey: {self.hotkey.upper()}",
                font=('Arial', 10), bg='white', fg='#888').pack(pady=(0, 25))
        self.make_button(inner, "Aufnahme starten", self.start_recording, 'blue').pack()
    
    def show_recording_state(self):
        self.clear_container()
        
        inner = tk.Frame(self.container, bg='white')
        inner.place(relx=0.5, rely=0.5, anchor='center')
        
        self.rec_dot = tk.Label(inner, text="⏺", font=('Arial', 36), bg='white', fg='#e53935')
        self.rec_dot.pack(pady=(0, 10))
        
        tk.Label(inner, text="Aufnahme läuft...",
                font=('Arial', 18, 'bold'), bg='white', fg='#333').pack(pady=(0, 15))
        
        self.time_label = tk.Label(inner, text="00:00",
                                   font=('Arial', 32, 'bold'), bg='white', fg='#333')
        self.time_label.pack(pady=(0, 25))
        
        # Button frame for Stop and Cancel
        btn_frame = tk.Frame(inner, bg='white')
        btn_frame.pack()
        
        self.make_button(btn_frame, "Stoppen", self.stop_recording, 'red').pack(side='left', padx=(0, 10))
        self.make_button(btn_frame, "Abbrechen", self.close_main_window, 'gray').pack(side='left')
        
        self.update_timer()
        self.pulse_dot()
    
    def show_loading_state(self):
        self.clear_container()
        
        inner = tk.Frame(self.container, bg='white')
        inner.place(relx=0.5, rely=0.5, anchor='center')
        
        self.spinner = tk.Canvas(inner, width=60, height=60, bg='white', highlightthickness=0)
        self.spinner.pack(pady=(0, 15))
        
        tk.Label(inner, text="Wird transkribiert...",
                font=('Arial', 14, 'bold'), bg='white', fg='#333').pack()
        
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
        tk.Label(header, text="✅", font=('Arial', 20), bg='white').pack(side='left', padx=(0, 10))
        tk.Label(header, text="Transkription abgeschlossen",
                font=('Arial', 14, 'bold'), bg='white', fg='#333').pack(side='left')
        
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
        
        self.result_text = tk.Text(text_frame, wrap='word', font=('Arial', 11),
                                   bg='#f9f9f9', fg='#333', relief='flat',
                                   padx=10, pady=10, yscrollcommand=scrollbar.set)
        self.result_text.pack(side='left', fill='both', expand=True)
        self.result_text.insert('1.0', text)
        scrollbar.config(command=self.result_text.yview)
    
    def show_error_state(self, msg):
        self.clear_container()
        
        inner = tk.Frame(self.container, bg='white')
        inner.place(relx=0.5, rely=0.5, anchor='center')
        
        tk.Label(inner, text="⚠️", font=('Arial', 36), bg='white').pack(pady=(0, 10))
        tk.Label(inner, text="Fehler", font=('Arial', 14, 'bold'), bg='white', fg='#e53935').pack(pady=(0, 8))
        tk.Label(inner, text=msg[:150], font=('Arial', 10), bg='white', fg='#666',
                wraplength=400).pack(pady=(0, 20))
        self.make_button(inner, "🔄 Nochmal", self.show_ready_state, 'blue').pack()
    
    # ==================== RECORDING ====================
    def audio_callback(self, indata, frames, time_info, status):
        if self.recording:
            self.audio_data.append(indata.copy())
    
    def start_recording(self):
        if self.recording:
            return
        try:
            self.recording = True
            self.audio_data = []
            self.start_time = time.time()
            self.update_tray('recording')
            self.stream = sd.InputStream(samplerate=self.sample_rate, channels=1,
                                         dtype='float32', callback=self.audio_callback)
            self.stream.start()
            self.root.after(0, self.show_main_window)
            self.root.after(50, self.show_recording_state)
        except Exception as e:
            self.recording = False
            self.update_tray('idle')
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
            self.update_tray('processing')
            self.root.after(0, self.show_loading_state)
            threading.Thread(target=self.transcribe, daemon=True).start()
        else:
            self.update_tray('idle')
            self.root.after(0, self.show_ready_state)
    
    def toggle_recording(self):
        if self.recording:
            self.root.after(0, self.stop_recording)
        else:
            self.root.after(0, self.start_recording)
    
    def update_timer(self):
        if self.recording and hasattr(self, 'time_label') and self.time_label.winfo_exists():
            try:
                elapsed = int(time.time() - self.start_time)
                m, s = divmod(elapsed, 60)
                self.time_label.config(text=f"{m:02d}:{s:02d}")
                self.main_window.after(200, self.update_timer)
            except:
                pass
    
    def pulse_dot(self):
        if self.recording and hasattr(self, 'rec_dot') and self.rec_dot.winfo_exists():
            try:
                c = self.rec_dot.cget('fg')
                self.rec_dot.config(fg='white' if c == '#e53935' else '#e53935')
                self.main_window.after(500, self.pulse_dot)
            except:
                pass
    
    def transcribe(self):
        tmp = None
        try:
            audio = np.concatenate(self.audio_data, axis=0)
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

            self.root.after(0, lambda: self.show_result_state(result.text))
        except Exception as e:
            if self.main_window: # Only show error if window is open
                 self.root.after(0, lambda: self.show_error_state(str(e)))
        finally:
            if tmp:
                try: os.unlink(tmp)
                except: pass
            self.update_tray('idle')
    
    # ==================== SETTINGS / QUIT ====================
    def open_settings(self):
        if self.show_setup_dialog():
            keyboard.unhook_all()
            keyboard.add_hotkey(self.hotkey, self.toggle_recording)
            if self.tray_icon:
                self.tray_icon.title = f"WhisperShroom ({self.hotkey.upper()})"
    
    def quit_app(self, *args):
        if self.recording:
            self.stop_recording()
        keyboard.unhook_all()
        if self.tray_icon:
            self.tray_icon.stop()
        self.root.quit()
        os._exit(0)
    
    # ==================== RUN ====================
    def run(self):
        if not self.api_key:
            if not self.show_setup_dialog():
                return
        
        keyboard.add_hotkey(self.hotkey, self.toggle_recording)
        
        menu = pystray.Menu(
            pystray.MenuItem('Aufnahme starten/stoppen', lambda: self.toggle_recording()),
            pystray.MenuItem(f'Hotkey: {self.hotkey.upper()}', lambda: None, enabled=False),
            pystray.Menu.SEPARATOR,
            pystray.MenuItem('Einstellungen', lambda: self.root.after(0, self.open_settings)),
            pystray.Menu.SEPARATOR,
            pystray.MenuItem('Beenden', self.quit_app)
        )
        
        self.tray_icon = pystray.Icon("WhisperShroom", self.create_tray_icon(),
                                      f"WhisperShroom ({self.hotkey.upper()})", menu)
        threading.Thread(target=self.tray_icon.run, daemon=True).start()
        self.root.mainloop()


if __name__ == "__main__":
    app = WhisperShroom()
    app.run()
