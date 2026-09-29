"""One-use exact fixes discovered by the integration tests; removed before commit."""
from pathlib import Path


def edit(path, before, after, count=1):
    p = Path(path)
    text = p.read_text(encoding='utf-8')
    if text.count(before) != count:
        raise RuntimeError(f'{path}: expected {count} exact anchors, found {text.count(before)}')
    p.write_text(text.replace(before, after), encoding='utf-8')


# InvalidDataException derives from SystemException, not IOException. Reject the bad
# source and try another verified transport rather than aborting the whole download.
edit('src/PingYi.Infrastructure/Runtime/RuntimeDownloader.cs',
     'error is HttpRequestException or OperationCanceledException or IOException)',
     'error is HttpRequestException or OperationCanceledException or IOException or InvalidDataException)')
edit('src/PingYi.Infrastructure/Runtime/RuntimeManager.cs',
     'error is ProviderException or IOException or HttpRequestException or System.ComponentModel.Win32Exception or OperationCanceledException)',
     'error is ProviderException or IOException or InvalidDataException or HttpRequestException or System.ComponentModel.Win32Exception or OperationCanceledException)')
Path(__file__).unlink()
