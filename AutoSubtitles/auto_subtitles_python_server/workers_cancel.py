import os
import time
import subprocess
import torch
import torchaudio
import json

# Фикс для torchaudio, если используется в специфичных окружениях
if not hasattr(torchaudio, "list_audio_backends"):
    torchaudio.list_audio_backends = lambda: []


def extract_and_convert_audio(input_media_path: str, output_wav_path: str):
    """
    Вызывает системный FFmpeg через subprocess.
    Извлекает/перекодирует любую медиа-дорожку в эталонный WAV:
    16000 Гц (частота Whisper/PyAnnote), 1 канал (моно), PCM 16-бит.
    """
    command = [
        "ffmpeg", "-y",  # Перезаписывать файл, если существует
        "-i", input_media_path,  # Входной медиафайл (любой кодек/контейнер)
        "-vn",  # Отключить видеопоток (если это видео)
        "-acodec", "pcm_s16le",  # Кодек PCM 16-бит
        "-ar", "16000",  # Частота дискретизации 16 кГц
        "-ac", "1",  # Конвертировать в моно-канал
        output_wav_path  # Путь сохранения результата
    ]
    print(f" Running FFmpeg for a file: {input_media_path}")

    # Выполняем команду, перенаправляя логи FFmpeg в pipe, чтобы не спамить в консоль
    result = subprocess.run(command, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    if result.returncode != 0:
        raise RuntimeError(f"FFmpeg Error: {result.stderr}")
    print(f" Audio successfully extracted and saved in: {output_wav_path}")


def run_transcription(audio_path, model_name, language, beam_size, offline_mode, return_dict, vad_filter, vad_threshold, min_silence_duration_ms, min_speech_duration_ms, speech_pad_ms, max_speech_duration_s):
    """
    Тяжелая транскрипция в отдельном процессе (для синхронного режима с диаризацией).
    """
    if offline_mode:
        os.environ["HF_HUB_OFFLINE"] = "1"
    else:
        os.environ.pop("HF_HUB_OFFLINE", None)

    from faster_whisper import WhisperModel
    print(f" The Whisper Process [{model_name}] running with beam_size={beam_size}...")
    t_start = time.perf_counter()

    model = WhisperModel(
        model_name,
        device="cpu",
        compute_type="int8",
        cpu_threads=3,
        local_files_only=offline_mode
    )

    lang_param = None if language == "auto" else language
    segments, _ = model.transcribe(
        audio_path,
        language=lang_param,
        word_timestamps=True,
        beam_size=beam_size,
        vad_filter=vad_filter,
        vad_parameters=dict(threshold=vad_threshold,
                            min_silence_duration_ms=min_silence_duration_ms,
                            min_speech_duration_ms=min_speech_duration_ms,
                            speech_pad_ms=speech_pad_ms,
                            max_speech_duration_s=max_speech_duration_s)
    )

    all_words = []
    for segment in segments:
        if segment.words:
            for word in segment.words:
                all_words.append({
                    'start': round(float(word.start), 2),
                    'end': round(float(word.end), 2),
                    'text': word.word
                })

    print(f" Whisper completed work for {time.perf_counter() - t_start:.2f} sec.")
    return_dict['text_segments'] = all_words


def run_diarization(audio_path, hf_token, offline_mode, return_dict):
    """
    Разделение по спикерам в отдельном процессе (для синхронного режима).
    """
    if offline_mode:
        os.environ["HF_HUB_OFFLINE"] = "1"
        os.environ["TRANSFORMERS_OFFLINE"] = "1"
    else:
        os.environ.pop("HF_HUB_OFFLINE", None)
        os.environ.pop("TRANSFORMERS_OFFLINE", None)

    if hf_token:
        os.environ["HUGGING_FACE_HUB_TOKEN"] = hf_token

    try:
        from pyannote.audio import Pipeline
        print(" The PyAnnote process is running...")
        t_start = time.perf_counter()
        torch.set_num_threads(3)

        # Работаем со стандартным онлайн-репозиторием пайплайна
        pipeline = Pipeline.from_pretrained("pyannote/speaker-diarization-3.0", token=hf_token)
        output = pipeline(audio_path)

        diarization_timestamps = []
        annotation = getattr(output, "speaker_diarization", output)
        for turn, _, speaker in annotation.itertracks(yield_label=True):
            diarization_timestamps.append({
                'speaker': speaker,
                'start': round(turn.start, 2),
                'end': round(turn.end, 2)
            })

        print(f" PyAnnote completed the work for {time.perf_counter() - t_start:.2f} sec.")
        return_dict['diar_segments'] = diarization_timestamps
    except Exception as e:
        print(f"CRITICAL ERROR IN THE DIARIZATION PROCESS: {str(e)}")
        return_dict['diar_segments'] = []


def combine_results(words_list, diarization_timestamps, max_pause):
    """
    Пословное микширование результатов Whisper и PyAnnote.
    """
    if not words_list:
        return []

    assigned_words = []
    for word in words_list:
        w_start = word['start']
        w_end = word['end']
        best_speaker = 'unknown'
        max_overlap = 0.0

        for segment in diarization_timestamps:
            overlap_start = max(w_start, segment['start'])
            overlap_end = min(w_end, segment['end'])
            overlap = overlap_end - overlap_start
            if overlap > max_overlap:
                max_overlap = overlap
                best_speaker = segment['speaker']

        assigned_words.append({
            'speaker': best_speaker,
            'start': w_start,
            'end': w_end,
            'text': word['text']
        })

    final_result = []
    current_speaker = assigned_words[0]['speaker']
    current_start = assigned_words[0]['start']
    current_end = assigned_words[0]['end']
    current_phrase = [assigned_words[0]['text']]

    for i in range(1, len(assigned_words)):
        word = assigned_words[i]
        time_gap = word['start'] - current_end

        if word['speaker'] == current_speaker and time_gap < max_pause:
            current_phrase.append(word['text'])
            current_end = word['end']
        else:
            final_result.append({
                'speaker': current_speaker,
                'start': current_start,
                'end': current_end,
                'text': "".join(current_phrase).strip()
            })
            current_speaker = word['speaker']
            current_start = word['start']
            current_end = word['end']
            current_phrase = [word['text']]

    final_result.append({
        'speaker': current_speaker,
        'start': current_start,
        'end': current_end,
        'text': "".join(current_phrase).strip()
    })
    return final_result


async def stream_transcription(audio_path, model_name, language, beam_size, offline_mode, request, task_id,
                               active_tasks_dict, vad_filter, vad_threshold, min_silence_duration_ms, min_speech_duration_ms, speech_pad_ms, max_speech_duration_s):
    """
    Асинхронный генератор для потоковой транскрипции (без диаризации).
    Возвращает сегменты текста сразу по мере их распознавания моделью.
    Поддерживает моментальную отмену при клике клиента или обрыве сети.
    """
    if offline_mode:
        os.environ["HF_HUB_OFFLINE"] = "1"
    else:
        os.environ.pop("HF_HUB_OFFLINE", None)

    from faster_whisper import WhisperModel
    print(f" [Streaming] Initialization of Whisper [{model_name}]...")

    # Ограничиваем потоки, чтобы не грузить CPU на 100%
    model = WhisperModel(
        model_name,
        device="cpu",
        compute_type="int8",
        cpu_threads=4,
        local_files_only=offline_mode
    )

    t_stream_start = time.perf_counter()

    lang_param = None if language == "auto" else language
    segments, _ = model.transcribe(
        audio_path,
        language=lang_param,
        word_timestamps=False,  # Для скорости построчного вывода пословные метки отключены
        beam_size=beam_size,
        vad_filter=vad_filter,
        vad_parameters=dict(threshold=vad_threshold,
                            min_silence_duration_ms=min_silence_duration_ms,
                            min_speech_duration_ms=min_speech_duration_ms,
                            speech_pad_ms=speech_pad_ms,
                            max_speech_duration_s=max_speech_duration_s)
    )

    for segment in segments:
        # ЗАЩИТА 1: Если клиент нажал на кнопку «Отмена»
        if task_id not in active_tasks_dict:
            print(f" [Streaming] Manual cancellation has been recorded for task {task_id}. Interrupting the cycle.")
            break
        # ЗАЩИТА 2: Если клиент закрыл вкладку
        if await request.is_disconnected():
            print(f" [Streaming] The network connection is terminated by the client. Stopping Whisper.")
            break

        # СЧИТАЕМ СКОРОСТЬ СТРИМИНГА НА ДАННОМ СЕГМЕНТЕ
        t_current_elapsed = time.perf_counter() - t_stream_start
        if t_current_elapsed > 0:
            current_speed = segment.end / t_current_elapsed
            print(f" [Streaming Stats] Processed up to: {segment.end:.2f}s | Current speed: {current_speed:.2f}x")

        # Формируем JSON-строку для каждого чанка текста
        chunk_data = {
            "start": round(segment.start, 2),
            "end": round(segment.end, 2),
            "text": segment.text.strip()
        }
        yield f"data: {json.dumps(chunk_data, ensure_ascii=False)}\n\n"

    # Чистим за собой глобальное хранилище, если сессия успешно завершилась сама
    active_tasks_dict.pop(task_id, None)
    print(" [Streaming] Transcription has been successfully completed or closed.")



def get_wav_duration(wav_path: str) -> float:
    """
    Возвращает точную длительность WAV-файла в секундах.
    Использует встроенную библиотеку wave, не требуя внешних утилит.
    """
    import wave
    try:
        with wave.open(wav_path, 'rb') as wav_file:
            frames = wav_file.getnframes()
            rate = wav_file.getframerate()
            duration = frames / float(rate)
            return round(duration, 2)
    except Exception as e:
        print(f" [Warning] Could not read WAV duration: {e}")
        return 0.0
