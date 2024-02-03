BeChat
=================================================

```
+------------------------------------------+
|                                          |
|       ____       ________          __    |
|      / __ )___  / ____/ /_  ____ _/ /_   |
|     / __  / _ \/ /   / __ \/ __ `/ __/   |
|    / /_/ /  __/ /___/ / / / /_/ / /_     |
|   /_____/\___/\____/_/ /_/\__,_/\__/     |
|                                          |
+------------------------------------------+
```
        
`BeChat` is a my proof-of-concept implementation of Peer-To-Peer chat application. It includes rendervouz server, UDP hole punching and a console client application.

Most modules of this project are implemented from scratch in order to test concepts learned through hours of researches.


Table of contents
-----------------

* [Introduction](#introduction)
* [Architecture](#introduction)
* [Features](#features)
* [Roadmap](#roadmap)
* [References](#roadmap)

Introduction
------------

BeChat consists from multiple projects:

- A console client application, `BeChat.Client`. 
- A server that supports jwt authentication, keeps track of users data and helps in connection (rendervouz in the terms of hole punching), `BeChat.Relay`
- A shared libraries defining protocol and messages (`BeChat.Logging`, `BeChat.Common`, `BeChat.Bencode`, `BeChat.Network`)
  - `BeChat.Logging` is a simple logging library that supports multiple log targets
  - `BeChat.Common`  is 
  - `BeChat.Bencode` is a [bencode](https://ru.wikipedia.org/wiki/Bencode) encoding and parsing/serialization library
  - `BeChat.Network` implements

Architecture
-----------

Overview
------------

Roadmap
------------

References 
------------

### Bencode encoding

### Reliable data transfer over UDP

### P2P connectivity
