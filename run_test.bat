@echo off
cd /d "C:\Users\user\Downloads\smartmusuem\Smart-Museum"
call .venv310\Scripts\activate.bat
python python/test_boxing_gestures.py
pause