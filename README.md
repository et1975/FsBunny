FsBunny [![Windows Build](https://ci.appveyor.com/api/projects/status/ssw7ttk4fo27jrs3?svg=true)](https://ci.appveyor.com/project/et1975/FsBunny) [![Mono/OSX build](https://travis-ci.org/et1975/FsBunny.svg?branch=master)](https://travis-ci.org/et1975/FsBunny) [![NuGet version](https://badge.fury.io/nu/FsBunny.svg)](https://badge.fury.io/nu/FsBunny)
=======

FsBunny implements a streaming API over RabbitMQ optimized for implementation of event-driven systems.

The core idea is that while there are many streams carrying many messages using many serializers, each stream is dedicated to a single type of message serialized using certain serializer. 

It works under the assumptions that:

- We never want to lose a message
- The exchange + topic tells us the message type (as well as serialization format)
- The consumer is long-lived and handles only one type of message
- The consumer decides when to pull the next message of a queue
- The publishers can be long- or short-lived and address any topic
- We have multiple serialization formats and may want to add new ones easily

These assumptions may not be suitable in all scenarios, but they map extremely well to event-driven processing.