# VideoEncodingAutomation
Automates video encoding using HandBrakeCLI.

## Usage

1. Download a release from [the Releases area](https://github.com/bp2008/VideoEncodingAutomation/releases) and extract to `C:\VideoEncodingAutomation\` or another location of your choosing.
2. Put [HandBrakeCLI](https://handbrake.fr/downloads2.php) somewhere, e.g. `C:\VideoEncodingAutomation\HandbrakeCLI.exe`.
3. Put [ffmpeg](https://ffmpeg.org/download.html) binaries (.exe, .dll, etc) in `C:\VideoEncodingAutomation\ffmpeg\`.  I like the full build (gpl) shared version for win64 from [here](https://github.com/BtbN/FFmpeg-Builds/releases).
4. Put [MediaInfo CLI](https://mediaarea.net/en/MediaInfo/Download/Windows) somewhere, e.g. `C:\VideoEncodingAutomation\MediaInfo.exe`.
5. Download starter encoder configurations from [here](https://github.com/bp2008/VideoEncodingAutomation/blob/main/Starter_Encode_Configuration.7z?raw=true). Create a folder somewhere that you have a lot of disk space, and extract the encoder configurations there.  You should now have a simple directory structure, e.g.
```
C:\
  -> Encode
      -> in
        -> q18
        -> q20
        -> q23
        -> q23-allaudio-allsubs
        -> q23-allsubs
        -> q23-SmartCrop
        -> q23-SmartCrop-allaudio-allsubs
        -> q23-SmartCrop-allsubs
        -> q26
```

The way this works is you drop video files into any of these input folders (`q18`, `q20`, etc) and the **VideoEncodingAutomation** process will automatically transcode the video according to the settings defined in `encoder.txt` within the folder.  Output goes in an `out` folder next to the `in` folder.

As an example, here is the `encoder.txt` from `q23-SmartCrop`.

```json
{
  "Encoder": "handbrake",
  "VideoEncoder": "x265",
  "VideoEncoderPreset": "medium",
  "Quality": 23,
  "HandbrakeCrop": "Smart",
  "AudioTrackSelection": {
    "AllTracks": false,
    "AllTracksNoCommentary": false,
    "AllEnglish": false,
    "AllEnglishNoCommentary": false
  },
  "SubtitleTrackSelection": {
    "AllTracks": false
  },
  "LimitedRange": false,
  "StartTimeSeconds": 0,
  "DurationSeconds": 0,
  "KeepInputForDebuggingAfterward": false
}
```

* `"Encoder": "handbrake"` instructs the app to use handbrake (currently this is the only allowed value of the field)
* `VideoEncoder` can be `"x265"` or `"x264"` or `"av1"`
* `VideoEncoderPreset` can be any of the encoder presets defined by the codec. See HandBrakeCLI documentation.
* `Quality` is the constant rate factor number defined by the codec. See HandBrakeCLI documentation.  For `x264` and `x265`, the range is `0-51` (lower is better quality), and a commonly used range is `18-23`.  For `av1`, the range is `0-63` (lower is better quality) and a commonly used range is `24-28`.
* `HandbrakeCrop` defines a value for HandbrakeCLI's `--crop` argument.  It could be `"0:0:0:0"` to do no cropping, `"280:280:0:0"` to remove 280 rows from the top and bottom, `""` to let handbrake crop automatically, or `"Smart"` to use my own custom cropping calculations which are especially conservative (high priority on preserving all image data, even at the cost of some black rows remaining).
* `KeepInputForDebuggingAfterward` is for debugging or testing purposes.  If it is false, the input videos are DELETED after transcoding has completed.  When using `"KeepInputForDebuggingAfterward": true`, it is advised to also set `LimitedRange` to true in order to use the `"StartTimeSeconds"` and `"DurationSeconds"` arguments in order to transcode small samples of video in relatively little time.

6. Run `VideoEncodingAutomation.exe` once, and then close it.  `Settings.cfg` will be created.
7. Open `Settings.cfg` and configure as needed.

```xml
<?xml version="1.0"?>
<Settings xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
  <videoStorageDir>C:\Encode\</videoStorageDir>
  <handbrakePath>HandBrakeCLI.exe</handbrakePath>
  <webPort>14580</webPort>
  <mediaInfoCLIPath>MediaInfo.exe</mediaInfoCLIPath>
</Settings>
```

8. Run `VideoEncodingAutomation.exe` again.  You can view the web interface via `http://localhost:14580/`

## Notes

I haven't tried in a long time, but this application may still work on Linux via [mono](https://www.mono-project.com/docs/getting-started/install/linux/) if you are so inclined.

## Building Source

Open the .sln file with the latest Visual Studio Community Edition. You'll need to get a copy of my [BPUtil](https://github.com/bp2008/BPUtil) library and repair the project references to this.
