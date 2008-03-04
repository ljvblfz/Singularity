From: Mark Aiken
Sent: Sunday, October 16, 2005 4:19 PM
To: Chris Hawblitzel
Subject: Running SPECweb

Here is how to run webfiles:

On Windows:
in Applications\Benchmarks\SpecWeb99\webfiles type
"msb"

On Singularity:

First run:
- "mkfs /dev/vol2"
- "fsmount /dev/vol2 /fs -n" [no caching]
- "wafgen99 -v 10 /fs" [Generates lots of files]
- "fsunmount"
- reboot to clear the log.

Second boot:
- "fsmount /dev/vol2 /fs"
- "webfiles -r:60"

Alternatively, to run an fixed benchmark size, use the -f:X, forced
iteration, parameter:
- "webfiles -f:20000"


