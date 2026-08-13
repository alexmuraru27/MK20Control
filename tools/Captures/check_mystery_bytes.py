import zlib

def find_json_end(data, start):
    depth = 0
    in_str = False
    esc = False
    end = start
    i = start
    while i < len(data):
        c = data[i]
        ch = chr(c) if c < 128 else None
        if in_str:
            if esc:
                esc = False
            elif ch == '\\':
                esc = True
            elif ch == '"':
                in_str = False
        else:
            if ch == '"':
                in_str = True
            elif ch in ('{', '['):
                depth += 1
            elif ch in ('}', ']'):
                depth -= 1
                if depth == 0:
                    end = i
                    break
        i += 1
    return end

files = [
    r'C:\Users\Alex\Desktop\MK20Software\ScreenKeyWindows_v1_1\theme\MK20\customTheme7buttonsSoftware.Theme',
    r'C:\Users\Alex\Desktop\MK20Software\ScreenKeyWindows_v1_1\theme\MK20\empty.Theme',
    r'C:\Users\Alex\Desktop\MK20Software\ScreenKeyWindows_v1_1\theme\MK20\customTheme5buttons.Theme',
]
for f in files:
    data = open(f, 'rb').read()
    idx = data.find(b'{')
    mystery_bytes = data[idx-2:idx]
    end = find_json_end(data, idx)
    json_bytes = data[idx:end+1]
    print(f)
    print('  mystery 2 bytes (LE uint16):', int.from_bytes(mystery_bytes, 'little'), 'hex', mystery_bytes.hex())
    print('  json length:', len(json_bytes))
    crc_full = zlib.crc32(json_bytes)
    print('  crc32(json) full:', crc_full, 'low16:', crc_full & 0xFFFF)
    print('  json length mod 65536:', len(json_bytes) % 65536)
    print('  mystery bytes as-is (BE):', int.from_bytes(mystery_bytes, 'big'))
