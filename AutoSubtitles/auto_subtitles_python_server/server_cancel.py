import os
import sys

# Отключает буферизацию вывода для мгновенной передачи логов в C#
sys.stdout.reconfigure(line_buffering=True)
sys.stderr.reconfigure(line_buffering=True)

os.environ["HF_HUB_DISABLE_XET"] = "1"

import time
import asyncio
from typing import Literal, Optional
from fastapi import FastAPI, HTTPException, Request
from fastapi.responses import StreamingResponse
from pydantic import BaseModel, Field
import workers_cancel


app = FastAPI(title="Whisper + PyAnnote Local Path API", strict_slashes=False)


ACTIVE_TASKS = {}

ModelType = Literal["tiny", "base", "small", "medium", "large-v1", "large-v2", "large-v3", "large"]
LanguageType = Literal["ru", "en", "auto"]


class TranscriptionRequest(BaseModel):
    task_id: str = Field(..., description="The unique task ID generated on the client (front-end) side")
    file_path: str = Field(..., description="The absolute path to the original media file on the PC (video or audio)")
    output_dir: str = Field(..., description="The path to the folder where to save the extracted WAV file")
    model: ModelType = Field("small", description="The size of the Faster Whisper model")
    language: LanguageType = Field("auto", description="Language (ru/en/auto)")
    beam_size: int = Field(3, ge=1, le=10, description="The size of the beam. 1 is fast, 5+ is more accurate, but slower")
    enable_diarization: bool = Field(True, description="Enable speaker separation")
    offline_mode: bool = Field(False, description="Forced offline mode for Hugging Face")
    hf_token: Optional[str] = Field(None, description="Hugging Face Token for PyAnnote")

    max_pause: float = Field(1.0, description="Max allowed pause inside one speaker's phrase")
    vad_filter: bool = Field(False, description="Enable Silero VAD filter to reduce noise/hallucinations")
    vad_threshold: float = Field(0.5, ge=0.0, le=1.0, description="VAD threshold (speech probability)")
    min_silence_duration_ms: int = Field(2000, ge=0, description="Space of silence required to split speech chunks in milliseconds")
    min_speech_duration_ms: int = Field(250, ge=0, description="Discard final speech chunks shorter than this value in milliseconds")
    speech_pad_ms: int = Field(400, ge=0, description="Padding added to the front and back of detected speech chunks in milliseconds")
    max_speech_duration_s: float = Field(float('inf'), description='Maximum allowed length of continuous speech chunks in seconds before forcing a split')


@app.post("/api/transcribe-local")
async def transcribe_local_file(transcription_request: TranscriptionRequest, request: Request):

    # 1. Проверяем физическое существование исходного файла
    if not os.path.exists(transcription_request.file_path):
        raise HTTPException(status_code=404, detail="The source file was not found.")

    # 2. Проверяем/создаем директорию назначения
    os.makedirs(transcription_request.output_dir, exist_ok=True)
    base_name = os.path.splitext(os.path.basename(transcription_request.file_path))[0]
    target_wav_path = os.path.join(transcription_request.output_dir, f"{base_name}_extracted.wav")

    # 3. Извлекаем аудио, если его нет в кэше
    if not os.path.exists(target_wav_path):
        workers_cancel.extract_and_convert_audio(transcription_request.file_path, target_wav_path)

    # Используем идентификатор задачи, который прислал фронтенд
    task_id = transcription_request.task_id

    # Защита от дублирования: проверяем, не запущена ли уже задача с таким ID
    if task_id in ACTIVE_TASKS:
        raise HTTPException(status_code=400, detail="The task with this ID is already running on the server.")

    # Выставляем переменные окружения, если запрошен оффлайн-режим
    if transcription_request.offline_mode:
        os.environ["HF_HUB_OFFLINE"] = "1"
        os.environ["TRANSFORMERS_OFFLINE"] = "1"
        os.environ["HF_DATASETS_OFFLINE"] = "1"
    else:
        os.environ.pop("HF_HUB_OFFLINE", None)
        os.environ.pop("TRANSFORMERS_OFFLINE", None)
        os.environ.pop("HF_DATASETS_OFFLINE", None)

    if transcription_request.hf_token:
        os.environ["HUGGING_FACE_HUB_TOKEN"] = transcription_request.hf_token

    # =========================================================================
    # РЕЖИМ 1: СТРИМИНГ (Диаризация отключена)
    # =========================================================================
    if not transcription_request.enable_diarization:
        # Регистрируем стриминг-сессию в глобальном трекере задач
        ACTIVE_TASKS[task_id] = ["active_stream", target_wav_path]

        return StreamingResponse(
            workers_cancel.stream_transcription(
                target_wav_path,
                transcription_request.model,
                transcription_request.language,
                transcription_request.beam_size,
                transcription_request.offline_mode,
                request,  # Передаем объект HTTP-запроса для авто-отмены при дисконнекте
                task_id,  # Передаем ID для ручной отмены по кнопке
                ACTIVE_TASKS,  # Ссылка на общий словарь задач
                transcription_request.vad_filter, transcription_request.vad_threshold, transcription_request.min_silence_duration_ms,
                transcription_request.min_speech_duration_ms, transcription_request.speech_pad_ms, transcription_request.max_speech_duration_s
            ),
            media_type="text/event-stream"
        )

    # =========================================================================
    # РЕЖИМ 2: СИНХРОННЫЙ JSON (Диаризация включена)
    # =========================================================================
    try:
        t_endpoint_start = time.perf_counter()

        manager = multiprocessing.Manager()
        shared_return_dict = manager.dict()
        processes = []

        process_whisper = multiprocessing.Process(
            target=workers_cancel.run_transcription,
            args=(target_wav_path, transcription_request.model, transcription_request.language,
                  transcription_request.beam_size, transcription_request.offline_mode, shared_return_dict,
                  transcription_request.vad_filter, transcription_request.vad_threshold, transcription_request.min_silence_duration_ms,
                  transcription_request.min_speech_duration_ms, transcription_request.speech_pad_ms, transcription_request.max_speech_duration_s
                  )
        )
        processes.append(process_whisper)

        process_pyannote = multiprocessing.Process(
            target=workers_cancel.run_diarization,
            args=(target_wav_path, transcription_request.hf_token, transcription_request.offline_mode,
                  shared_return_dict)
        )
        processes.append(process_pyannote)

        # Записываем тяжелые процессы в глобальное хранилище ДО их физического старта
        ACTIVE_TASKS[task_id] = [process_whisper, process_pyannote, target_wav_path]

        for p in processes:
            p.start()

        # Асинхронно мониторим состояние процессов в неблокирующем цикле while
        while process_whisper.is_alive() or process_pyannote.is_alive():
            # Если клиент нажал на фронтенде кнопку «Отмена», эндпоинт /api/cancel удалит эту задачу из словаря
            if task_id not in ACTIVE_TASKS:
                raise HTTPException(status_code=499, detail="The processing was forcibly interrupted by the user.")

            # Даем FastAPI переключаться на обработку других HTTP-запросов (включая эндпоинт отмены)
            await asyncio.sleep(0.5)

        # Забираем результаты вычислений из разделяемой памяти процессов
        words_list = shared_return_dict.get('text_segments', [])
        diarization_timestamps = shared_return_dict.get('diar_segments', [])

        t_total_elapsed = time.perf_counter() - t_endpoint_start
        audio_duration = workers_cancel.get_wav_duration(target_wav_path)

        if t_total_elapsed > 0 and audio_duration > 0:
            total_speed = audio_duration / t_total_elapsed
            print(f" [Stats] Total processing completed in {t_total_elapsed:.2f} sec.")
            print(f" [Stats] Audio duration: {audio_duration:.2f} sec. Processing speed: {total_speed:.2f}x")

        # Микшируем слова и спикеров в единый финальный текст
        final_result = workers_cancel.combine_results(words_list, diarization_timestamps, max_pause=transcription_request.max_pause)
        return {
            "status": "success",
            "task_id": task_id,
            "extracted_audio_path": target_wav_path,
            "segments": final_result
        }

    except Exception as e:
        # Если произошла ошибка или отмена — подчищаем за собой сгенерированный WAV файл
        if os.path.exists(target_wav_path) and task_id in ACTIVE_TASKS:
            try:
                os.remove(target_wav_path)
            except Exception as file_err:
                print(f" Couldn't delete WAV file during crash: {file_err}")
        raise e
    finally:
        # В любом сценарии обязательно вычищаем задачу из памяти сервера
        ACTIVE_TASKS.pop(task_id, None)


# =========================================================================
# ЕДИНЫЙ ЭНДПОИНТ ДЛЯ КНОПКИ «ОТМЕНА» (Вызывается по клику на фронтенде)
# =========================================================================
class CancelRequest(BaseModel):
    task_id: str


@app.post("/api/cancel")
def cancel_transcription(cancel_req: CancelRequest):
    task_id = cancel_req.task_id

    if task_id not in ACTIVE_TASKS:
        raise HTTPException(status_code=404, detail="The task was not found, has already been completed, or was canceled earlier.")

    # Извлекаем данные задачи и сразу удаляем её из трекера
    task_data = ACTIVE_TASKS.pop(task_id)

    # Случай А: Отменяем стриминг
    if isinstance(task_data, list) and task_data[0] == "active_stream":
        target_wav_path = task_data[1]
        print(f" [Cancel] The manual cancellation signal has been transmitted for the active stream: {task_id}")

    # Случай Б: Отменяем фоновые тяжелые процессы multiprocessing (JSON режим)
    else:
        process_whisper, process_pyannote, target_wav_path = task_data
        for proc in [process_whisper, process_pyannote]:
            if proc and proc.is_alive():
                print(f" [Cancel] Forcible termination of the PID process {proc.pid}")
                proc.terminate()  # Мягкое прерывание SIGTERM
                proc.join()  # Освобождаем ресурсы ОС, убирая процесс из таблицы зомби

    # Чистим временные аудиофайлы, так как обработка не завершена
    if os.path.exists(target_wav_path):
        try:
            os.remove(target_wav_path)
            print(f" [Cancel] Temporary file {target_wav_path} successfully deleted when canceling the request.")
        except Exception as e:
            print(f" [Error] Couldn't erase temporary WAV file: {e}")

    return {"status": "cancelled", "message": f"The {task_id} task was successfully stopped by the server."}


if __name__ == "__main__":
    import multiprocessing
    import argparse
    import uvicorn
    import shutil
    import sys
    import os

    # 1. Защита от fork-бомбы для PyInstaller
    multiprocessing.freeze_support()

    # 2. Безопасный парсинг аргументов
    parser = argparse.ArgumentParser(description="Whisper + PyAnnote Local API Server")
    parser.add_argument("--ffmpeg-path", type=str, default=None, help="Absolute path to the ffmpeg executable directory")
    args, unknown = parser.parse_known_args()

    # 3. Инжектируем путь, если он передан из WPF
    if args.ffmpeg_path:
        if os.path.isfile(args.ffmpeg_path):
            ffmpeg_dir = os.path.dirname(args.ffmpeg_path)
        else:
            ffmpeg_dir = args.ffmpeg_path

        if os.path.exists(ffmpeg_dir):
            os.environ["PATH"] = ffmpeg_dir + os.pathsep + os.environ.get("PATH", "")
            print(f" [Init] Custom FFmpeg path injected into PATH: {ffmpeg_dir}")
        else:
            print(f" [Warning] Provided FFmpeg path does not exist: {ffmpeg_dir}")

    # 4. КРИТИЧЕСКАЯ ПРОВЕРКА: Проверяем, видит ли ОС хоть какой-нибудь ffmpeg
    # Функция shutil.which ищет исполняемый файл в текущем PATH
    if not shutil.which("ffmpeg"):
        print(" [CRITICAL ERROR] FFmpeg executable was not found anywhere (neither in passed path nor in system PATH)!")
        print(" [CRITICAL ERROR] The server cannot start without FFmpeg. Shutting down.")
        sys.exit(1) # Завершаем процесс с кодом ошибки, WPF сразу это увидит
    else:
        print(f" [Init] FFmpeg successfully detected at: {shutil.which('ffmpeg')}")

    # 5. Запуск веб-сервера uvicorn, если проверка прейдена
    config = uvicorn.Config(app, host="127.0.0.1", port=8000, reload=False, log_level="info")
    server = uvicorn.Server(config)
    server.run()
