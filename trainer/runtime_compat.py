# Copyright (c) 2026 eirros. All rights reserved.
"""Common runtime APIs used by the portable Windows builds."""
import asyncio
from contextvars import copy_context
from functools import partial
import hashlib
import sys

if sys.version_info >= (3, 11):
    from asyncio import timeout
    TLS_STREAM_OPTIONS = {'ssl_shutdown_timeout': 1}
else:
    from async_timeout import timeout
    TLS_STREAM_OPTIONS = {}

if sys.version_info >= (3, 9):
    from asyncio import to_thread
else:
    async def to_thread(function, /, *args, **kwargs):
        call = partial(copy_context().run, function, *args, **kwargs)
        return await asyncio.get_running_loop().run_in_executor(None, call)


def file_sha256(path):
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(256 * 1024), b''):
            digest.update(block)
    return digest.hexdigest()
