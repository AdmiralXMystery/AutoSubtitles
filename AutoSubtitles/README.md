# AutoSubtitles

**AutoSubtitles** is a desktop application for Windows (WPF, C#) designed for automatic transcription of video and audio into subtitles, with support for speaker diarization, editing, and export.

## Features

- 🎙️ **Automatic transcription** of video and audio files using local AI models:
  - **Faster Whisper** (CTranslate2) — fast transcription
  - **Whisper.net** (GGML) — local transcription via native C# libraries
- 👥 **Speaker diarization** (speaker identification) via the built‑in Python server (PyAnnote.audio)
- ✏️Editing subtitles in tabular form:
  - Changing the text and start/end times
  - Merging, splitting, and deleting lines
  - Inserting empty lines
  - Renaming speakers
- 🎬 **Built‑in media player** with video and audio support
- 🌊 **Sound wave visualization** for audio files
- 📤 **Export subtitles** in TXT and JSON formats with settings for displaying timestamps, speakers, and text.
- 📥 **Import subtitles** from JSON.
- ⚙️ **Flexible settings** for transcription parameters (VAD, beam size, pauses, etc.).
- 🔄 **Automatic model download** from HuggingFace.
- 🌐 **Local Python server** for AI processing, launched automatically.

## System Requirements

- **OS**: Windows 10/11 (x64)
- **.NET**: .NET 8.0 or higher
- **Python**: not required separately — the server is supplied as an executable file
- **RAM**: 8+ GB is recommended (16+ GB for larger models)
- **CPU** only mode

## Usage

### File upload
- Click **Open** or use `Ctrl+O`
- Supported formats: MP4, WMV, AVI, MP3, WAV, AAC, M4A, FLAC, OGG

### Transcription
1. Select the speakers’ language (RU, EN, or Auto) from the list below the timeline.
2. Select a model in the Models tab.
3. If necessary, adjust the parameters in the Advanced AI tab.
3. Click **Start Transcription**
4. Wait for completion — the subtitles will appear in the table.

### Editing
- **Double-click** on a line — editing
- **Right-click** on a line — context menu (merge, split, delete, insert)
- **Alt + Right-click** — go to the start time of the subtitle
- **Spacebar** — play/pause

### Export
- Click **Export** or use `Ctrl+E`
- Select the format (TXT/JSON) and configure the field display
- Rename the speakers if necessary
- Save the file

### Building the Python server.
```bash
cd auto_subtitles_python_server
pip install -r requirements.txt
```
Copy the build command from the build_command.txt file and run it.