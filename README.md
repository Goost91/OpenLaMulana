# OpenLaMulana
A C#, cross-platform port of La-Mulana Classic. Written using MonoGame in Visual Studio.

# Libs
- [MonoGame](https://www.monogame.net/) | NuGet

# Running
The game assets are not included on this repo. You must download the original game and move the /data/, /graphics/, /music/, and /sound/ folders to this game engine's /Content/ folder. All of the .bmps in the /graphics/ folder must also be converted from .bmp to .png.

For the time being, I will host the assets here: [MEGA](https://mega.nz/file/XbRQXTDL#88CDYFwG47P7G6LBwWmYau0k6fyVOGxw1aR_zM-Aw88)

You will also need to install .NET runtime 5.0 if you don't already have it: [https://dotnet.microsoft.com/download/dotnet/5.0](https://dotnet.microsoft.com/download/dotnet/5.0)

Then, just run the .exe and you should be good to go!

# Compiling
- Clone the repo
- Copy the original game's assets (/data/, /graphics/, /music/, and /sound/) to the repo's /Content/ folder
- Open the .sln in Visual Studio; Configure the .sln so that it will "Always Copy" all of the original game's assets to the new build folder.
- Compile and Build
