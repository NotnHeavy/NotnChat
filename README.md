# NotnChat
One day I was bored so I decided to work on my first own chat client/server. This is like the bare minimum of a working chat, but it was a nice project to work on. Since this establishes SSL security, you'll need to generate your own SSL certificate to use. Everything else should work well from here.

## Known bugs
- The custom WriteLine/ReadLine functions (geared towards allowing user input to be spaced out properly whenever a message is received) don't work properly outside Windows and the overall functionality of the app will freeze unless a character is pressed.

This uses .NET 6.0 and the binaries are x64.
