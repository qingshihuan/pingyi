"""A private-session D-Bus fixture, never a replacement for a user's real portal.
Run only under dbus-run-session for native interop regression tests.
"""
import os
import struct
import tempfile
import zlib
from pathlib import Path
from gi.repository import Gio, GLib

XML = """<node>
<interface name='org.freedesktop.portal.Screenshot'>
 <method name='Screenshot'><arg type='s' direction='in'/><arg type='a{sv}' direction='in'/><arg type='o' direction='out'/></method>
</interface>
<interface name='org.freedesktop.portal.Request'>
 <method name='Close'/><signal name='Response'><arg type='u'/><arg type='a{sv}'/></signal>
</interface></node>"""
node = Gio.DBusNodeInfo.new_for_xml(XML)
request_count = 0
# A deterministic 2x2 RGB PNG, made locally; no image assets or downloads.
def chunk(kind, data):
    return struct.pack('!I', len(data)) + kind + data + struct.pack('!I', zlib.crc32(kind + data) & 0xffffffff)
png = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('!IIBBBBB',2,2,8,2,0,0,0)) + chunk(b'IDAT', zlib.compress((b'\0' + b'\xff\0\0' * 2) * 2)) + chunk(b'IEND', b'')

def call(connection, sender, path, interface, method, parameters, invocation):
    global request_count
    if method == 'Close':
        Path(os.environ['PINGYI_PORTAL_CLOSED']).touch()
        invocation.return_value(GLib.Variant('()', ()))
        return
    request_count += 1
    parent, options = parameters.unpack()
    assert options['interactive'] is True and options['modal'] is False
    handle = '/org/freedesktop/portal/desktop/request/' + sender[1:].replace('.', '_') + '/' + options['handle_token']
    connection.register_object(handle, node.interfaces[1], call, None, None)
    values = {}
    response = 1 if request_count == 2 else 2 if request_count == 3 else 0
    if request_count not in (2, 3, 4):
        fd, file_name = tempfile.mkstemp(prefix='pingyi-portal-fixture-', suffix='.png')
        with os.fdopen(fd, 'wb') as image: image.write(png)
        values['uri'] = GLib.Variant('s', Path(file_name).as_uri())
    if request_count != 4:
        # Exercise early response race, before the original Screenshot call reply.
        connection.emit_signal(sender, handle, 'org.freedesktop.portal.Request', 'Response', GLib.Variant('(ua{sv})', (response, values)))
    invocation.return_value(GLib.Variant('(o)', (handle,)))

def acquired(connection, _name):
    connection.register_object('/org/freedesktop/portal/desktop', node.interfaces[0], call, None, None)
def ready(connection, _name):
    Path(os.environ['PINGYI_PORTAL_READY']).touch()
Gio.bus_own_name(Gio.BusType.SESSION, 'org.freedesktop.portal.Desktop', Gio.BusNameOwnerFlags.NONE, acquired, ready, None)
GLib.MainLoop().run()
