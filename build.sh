#!/bin/bash
set -e
API=/usr/lib/mono/4.5-api
mcs -noconfig -nostdlib -lib:$API -r:$API/mscorlib.dll -r:$API/System.dll -r:$API/System.Drawing.dll -r:$API/System.Windows.Forms.dll \
  -target:winexe -optimize+ -warn:4 -win32icon:icon.ico -resource:icon.ico,app.ico -out:MathSnipBoard.exe src/*.cs
