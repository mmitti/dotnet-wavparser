# Modify version
This is modified for performance.
Change double to short and remove float wave support.

# original information

# WAV Parser
[![NuGet version (NokitaKaze.WAVParser)](https://img.shields.io/nuget/v/NokitaKaze.WAVParser.svg?style=flat)](https://www.nuget.org/packages/NokitaKaze.WAVParser/)
[![Build status](https://ci.appveyor.com/api/projects/status/3fgpod9vvmgu45v8/branch/master?svg=true)](https://ci.appveyor.com/project/nokitakaze/dotnet-wavparser/branch/master)
[![Test status](https://img.shields.io/appveyor/tests/nokitakaze/dotnet-wavparser.svg)](https://ci.appveyor.com/project/nokitakaze/dotnet-wavparser/branch/master)
[![Downloads](https://img.shields.io/nuget/dt/NokitaKaze.WAVParser.svg)](https://www.nuget.org/packages/NokitaKaze.WAVParser)

Yet another parser for wave files. I just like to write my own code🚲.

This library could read 8/16/24/32/64-bit audio with WAVE_FORMAT_PCM (pcm_u8, pcm_s16le, pcm_s32le, pcm_s24le, pcm_s32le, pcm_s64le) format and 32/64-float bit with WAVE_FORMAT_IEEE_FLOAT (pcm_f32le, pcm_f64le).

Main reason for the existence of this library is to support [other library](https://github.com/nokitakaze/VSTAudioProcessor).

## Using

You could read any media file, but you need to convert it to WAV.
```bash
ffmpeg -i some-media-file.raw -sn -vn -c:a pcm_s16le temporary.wav
```

### Read
```csharp
var parser = new NokitaKaze.WAVParser.WAVParser("input.wav");
Console.WriteLine("Channels count:\t{0}", parser.ChannelCount);
Console.WriteLine("AudioFormat:   \t{0}", parser.AudioFormat);
Console.WriteLine("BlockAlign:    \t{0}", parser.BlockAlign);
Console.WriteLine("SampleRate:    \t{0}Hz", parser.SampleRate);
Console.WriteLine("BitsPerSample: \t{0}", parser.SamplesCount);
Console.WriteLine("Duration:      \t{0}", parser.Duration);

foreach (var channelSamples in reReader.Samples)
{
    foreach (var sample in channelSamples)
    {
        // Hint: All samples are doubles for double precision processing
        Console.Write("{0:F8}, ", sample);
    }
}
```

```csharp
// method 2
Stream stream;
var parser = new NokitaKaze.WAVParser.WAVParser(stream);

// method 3
var rawRiffStream = System.IO.File.ReadAllBytes("input.wav");
var parser = new NokitaKaze.WAVParser.WAVParser(byteArray);
```

### Write
```csharp
// method #1
var byteArray = parser.GetDataAsRiff();

// method #2
parser.Save("output.wav");

// method #3
Stream randomStream = ...;
parser.Save(randomStream);
```

### Create new
```csharp
var rnd = new System.Random.Random();

var parser = new NokitaKaze.WAVParser.WAVParser();
parser.ChannelCount = 1;
parser.Samples = new List<List<double>>() {new List<double>()};
parser.BlockAlign = (ushort) (parser.ChannelCount * parser.BitsPerSample / 8);

for (int i = 0; i < 1000; i++)
{
    // random samples to stream
    parser.Samples[0].Add(rnd.NextDouble() * 2 - 1);
}

parser.Save(...);
```

## Music License
Public domain:
- Johann Sebastian Bach's "Air on G String" (from Orchestral Suite no. 3) by the USAF Strings (US Air Force Band)

CC BY-SA 3.0 (Creative Commons Attribution-ShareAlike 3.0):
- Antonín Dvořák's "Largo" from ("From the New World", Symphony no. 9 in Em) by Barbara Schubert (DuPage Symphony)
