BeChat
=================================================

+------------------------------------------+
|                                          |
|       ____       ________          __    |
|      / __ )___  / ____/ /_  ____ _/ /_   |
|     / __  / _ \/ /   / __ \/ __ `/ __/   |
|    / /_/ /  __/ /___/ / / / /_/ / /_     |
|   /_____/\___/\____/_/ /_/\__,_/\__/     |
|                                          |
+------------------------------------------+
        
BeChat is a my proof-of-concept implementation of Peer-To-Peer chat application. It includes rendervouz server, UDP hole punching and a console client application.


Table of contents
-----------------

* [Introduction](#introduction)
* [Features](#features)
* [Roadmap](#roadmap)

Introduction
------------

BeChat consists from multiple projects:

- A console client application, BeChat.Client. 
- A server that supports jwt authentication, keeps track of users data and helps in connection (rendervouz in the terms of hole punching), BeChat.Relay
- A shared libraries defining protocol and messages (BeChat.Logging, BeChat.Common, BeChat.Bencode, BeChat.Network)

Architecture
-----------

Features
------------

Roadmap
------------

References 
------------
