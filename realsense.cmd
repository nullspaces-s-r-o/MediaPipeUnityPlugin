mkdir C:\TEMP

REM Download librealsense if id does not exist
if not exist C:\TEMP\librealsense.zip (
  curl -L --retry 4 --connect-timeout 10 https://github.com/IntelRealSense/librealsense/archive/refs/tags/v2.56.3.zip -o C:\TEMP\librealsense.zip
)

tar -xf C:\TEMP\librealsense.zip -C C:\TEMP
  
del C:\TEMP\librealsense.zip

REM Delete build folder if it exists
rmdir /s /q C:\TEMP\librealsense-2.56.3\build

cmake ^
  -S C:\TEMP\librealsense-2.56.3 ^
  -B C:\TEMP\librealsense-2.56.3\build ^
  -DBUILD_CSHARP_BINDINGS=ON ^
  -DBUILD_UNITY_BINDINGS=ON ^
  -DBUILD_SHARED_LIBS=ON ^
  -DDOTNET_VERSION_LIBRARY=3.5 ^
  -DCMAKE_GENERATOR_PLATFORM=x64 ^
  -DUNITY_PATH="C:/Program Files/Unity/Hub/Editor/6000.0.34f1/Editor/Unity.exe"

cmake ^
  --build C:\TEMP\librealsense-2.56.3\build ^
  --config Release