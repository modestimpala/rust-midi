# rust-midi
A rust midi player in C# using DryWetMidi

# Usage 
To use you must have a virtual midi channel installed. I use loopMIDI. Make a channel named "rust" before using.
Then simply point the program to your midi folder and either select a file or click random file, then click play. 
It will route the midi file through the virtual midi channel and your Rust instruments should play the song.
While playing, click the save icon to save a song to your favorites list. To play from the favorites, 
ensure "Random File" is not checked and select a favorite then hit play.
