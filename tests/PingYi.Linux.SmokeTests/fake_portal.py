"""A private session-bus portal double; only synthetic pixels, no network/model/user data.
Production uses native libgio. This test verifies its wire protocol/callback lifetime.
"""
from pathlib import Path
import os
import struct
import zlib
from gi.repository import Gio, GLib

ROOT = Path(os.environ['PINGYI_PORTAL_TEST_DIR'])
ROOT.mkdir(parents=True, exist_ok=True)
DEST = 'org.freedesktop.portal.Desktop'
PATH = '/org/freedesktop/portal/desktop'
EVENTS = ROOT / 'events'
EVENTS.write_text('')
def event(value):
    with EVENTS.open('a') as stream:
        stream.write(value + '\n')
def mode():
    return (ROOT / 'mode').read_text().strip() if (ROOT / 'mode').exists() else 'success'
def chunk(kind, value):
    return struct.pack('!I', len(value)) + kind + value + struct.pack('!I', zlib.crc32(kind + value) & 0xffffffff)
(ROOT / 'synthetic.png').write_bytes(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('!2I5B', 64, 40, 8, 2, 0, 0, 0)) +
    chunk(b'IDAT', zlib.compress((b'\x00' + b'\x35\x87\xda' * 64) * 40)) + chunk(b'IEND', b''))
xml = '''<node>
<interface name="org.freedesktop.host.portal.Registry"><method name="Register"><arg type="s" direction="in"/><arg type="a{sv}" direction="in"/></method></interface>
<interface name="org.freedesktop.portal.Screenshot"><method name="Screenshot"><arg type="s" direction="in"/><arg type="a{sv}" direction="in"/><arg type="o" direction="out"/></method></interface>
<interface name="org.freedesktop.portal.GlobalShortcuts">
<method name="CreateSession"><arg type="a{sv}" direction="in"/><arg type="o" direction="out"/></method>
<method name="BindShortcuts"><arg type="o" direction="in"/><arg type="a(sa{sv})" direction="in"/><arg type="s" direction="in"/><arg type="a{sv}" direction="in"/><arg type="o" direction="out"/></method>
<signal name="Activated"><arg type="o"/><arg type="s"/><arg type="t"/><arg type="a{sv}"/></signal>
<signal name="ShortcutsChanged"><arg type="o"/><arg type="a(sa{sv})"/></signal>
</interface>
<interface name="org.freedesktop.portal.Request"><method name="Close"/><signal name="Response"><arg type="u"/><arg type="a{sv}"/></signal></interface>
<interface name="org.freedesktop.portal.Session"><method name="Close"/><signal name="Closed"><arg type="a{sv}"/></signal></interface>
</node>'''
info = Gio.DBusNodeInfo.new_for_xml(xml)
connection = Gio.bus_get_sync(Gio.BusType.SESSION, None)
connection.call_sync('org.freedesktop.DBus', '/org/freedesktop/DBus', 'org.freedesktop.DBus', 'RequestName',
    GLib.Variant('(su)', (DEST, 0)), GLib.VariantType.new('(u)'), Gio.DBusCallFlags.NONE, 5000, None)
registered = set()
objects = []
def shortcut(key):
    return [('capture', {'trigger_description': GLib.Variant('s', key)})]
def emit(path, iface, member, signature, args, receiver):
    connection.emit_signal(receiver, path, iface, member, GLib.Variant(signature, args))
    connection.flush_sync(None)
    return False

def request(sender, options, invocation, code=0, result=None, hanging=False):
    assert options.get('handle_token')
    handle = PATH + '/request/' + sender[1:].replace('.', '_') + '/' + options['handle_token']
    objects.append(connection.register_object(handle, info.lookup_interface('org.freedesktop.portal.Request'), dispatch, None, None))
    # Deliberately send a very early Response before the method return to test the
    # subscribe-before-call path; a caller subscribing after reply loses this signal.
    if not hanging:
        emit(handle, 'org.freedesktop.portal.Request', 'Response', '(ua{sv})', (code, result or {}), sender)
    invocation.return_value(GLib.Variant('(o)', (handle,)))

def dispatch(conn, sender, path, interface, method, parameters, invocation):
    try:
        args = parameters.unpack()
        if method == 'Register':
            assert args[0] in ('pingyi', 'pingyi-complete')
            registered.add(sender); event('register'); invocation.return_value(GLib.Variant('()', ())); return
        if method == 'Close':
            event('request-close' if interface.endswith('Request') else 'session-close')
            invocation.return_value(GLib.Variant('()', ())); return
        assert sender in registered, 'Registry must precede other calls on this connection'
        if method == 'Screenshot':
            assert parameters.get_type_string() == '(sa{sv})'
            assert args[1]['interactive'] is True
            event('screenshot')
            state = mode()
            request(sender, args[1], invocation, code=1 if state == 'cancel' else 2 if state == 'denied' else 0,
                result={'uri': GLib.Variant('s', (ROOT / 'synthetic.png').as_uri())}, hanging=state == 'hang')
        elif method == 'CreateSession':
            if mode() == 'unsupported':
                invocation.return_dbus_error('org.freedesktop.DBus.Error.UnknownMethod', 'No GlobalShortcuts portal'); return
            assert parameters.get_type_string() == '(a{sv})'
            session = PATH + '/session/' + sender[1:].replace('.', '_') + '/' + args[0]['session_handle_token']
            objects.append(connection.register_object(session, info.lookup_interface('org.freedesktop.portal.Session'), dispatch, None, None))
            request(sender, args[0], invocation, result={'session_handle': GLib.Variant('s', session)})
        elif method == 'BindShortcuts':
            assert parameters.get_type_string() == '(oa(sa{sv})sa{sv})'
            session, shortcuts, parent, options = args
            assert shortcuts[0][0] == 'capture' and shortcuts[0][1]['preferred_trigger'] == 'CTRL+ALT+SHIFT+d'
            assert shortcuts[0][1]['description']
            request(sender, options, invocation, result={'shortcuts': GLib.Variant('a(sa{sv})', shortcut('Ctrl+Alt+Shift+K'))})
            GLib.timeout_add(300, emit, PATH, 'org.freedesktop.portal.GlobalShortcuts', 'Activated', '(osta{sv})', (session, 'capture', 1, {}), sender)
            GLib.timeout_add(600, emit, PATH, 'org.freedesktop.portal.GlobalShortcuts', 'ShortcutsChanged', '(oa(sa{sv}))', (session, shortcut('Ctrl+Alt+Shift+L')), sender)
            GLib.timeout_add(1100, emit, session, 'org.freedesktop.portal.Session', 'Closed', '(a{sv})', ({},), sender)
    except Exception as error:
        event('bad-contract:' + type(error).__name__)
        invocation.return_dbus_error('org.freedesktop.portal.Error.Failed', 'Test protocol assertion failed')

for interface in ('org.freedesktop.host.portal.Registry', 'org.freedesktop.portal.Screenshot', 'org.freedesktop.portal.GlobalShortcuts'):
    objects.append(connection.register_object(PATH, info.lookup_interface(interface), dispatch, None, None))
(ROOT / 'ready').write_text('ready')
GLib.MainLoop().run()
