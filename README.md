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
* [Overview](#features)
* [Roadmap](#roadmap)
* [References](#roadmap)

Introduction
------------

BeChat consists from multiple projects:

- A console client application, `BeChat.Client`. 
- A server that supports jwt authentication, keeps track of users data and helps in connection (rendervouz in the terms of hole punching), `BeChat.Relay`
- A shared libraries defining protocol and messages (`BeChat.Logging`, `BeChat.Common`, `BeChat.Bencode`, `BeChat.Network`)
  - `BeChat.Logging` is a simple logging library that supports multiple log targets
  - `BeChat.Common`  is a library that contains system entities definitions and protocol implementation
  - `BeChat.Bencode` is a [bencode](https://ru.wikipedia.org/wiki/Bencode) encoding and parsing/serialization library
  - `BeChat.Network` implements reliable connection with end-to-end encryption (using `libsodium` .NET wrapper `NSec`) over UDP protocol 

Architecture
-----------

Overview
------------

ToDo
------------

- Dockerfiles for Relay and database
- GUI client
- Keep message history
- File transfer
- Voice chat

References 
------------

### Bencode encoding

[BEncode specification](https://wiki.theory.org/BitTorrentSpecification#Bencoding)

------------

### Reliable data transfer over UDP

- [Automatic repeat requests](https://en.wikipedia.org/wiki/Automatic_repeat_request)
- [Selective repeat protocol](https://en.wikipedia.org/wiki/Selective_Repeat_ARQ)
- [Virtual connection over UDP](https://gafferongames.com/post/virtual_connection_over_udp/)
- [Reliable UDP protocol](https://hackernoon.com/unity-realtime-multiplayer-part-3-reliable-udp-protocol)

------------

### Encryption

- [Elliptic Diffie-Hellman key exchange protocol](https://cryptobook.nakov.com/asymmetric-key-ciphers/ecdh-key-exchange)
- [Elliptic curve cryptography](https://cryptobook.nakov.com/asymmetric-key-ciphers/elliptic-curve-cryptography-ecc)

------------

### P2P connectivity
[Peer-to-Peer Communication Across Network Address Translators](https://bford.info/pub/net/p2pnat/index.html)

------------
