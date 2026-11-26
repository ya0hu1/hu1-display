"""Offline lint for loose WPF XAML and its embedded two-language catalog."""
from pathlib import Path
import json
import re
import xml.etree.ElementTree as ET

src = Path(__file__).resolve().parents[1] / 'src'
entries = json.loads((src / 'UiStrings.json').read_text(encoding='utf-8'))['Entries']
keys, sources = set(), set()
for item in entries:
    key, zh, en = (item[field] for field in ('Key', 'Zh', 'En'))
    assert key and key not in keys and zh and zh not in sources and en, key
    assert sorted(re.findall(r'\{\d+\}', zh)) == sorted(re.findall(r'\{\d+\}', en)), key
    keys.add(key)
    sources.add(zh)
xaml = (src / 'Studio.xaml').read_text(encoding='utf-8')
root = ET.fromstring(xaml)
ns = '{http://schemas.microsoft.com/winfx/2006/xaml}'
local_keys = {node.get(ns + 'Key') for node in root.iter()}
missing = set(re.findall(r'\{DynamicResource ([\w]+)\}', xaml)) - keys - local_keys
assert not missing, missing
brand = next(item for item in entries if item['Key'] == 'Text289')
assert (brand['Zh'], brand['En']) == ('晖映', 'hu1 display')
print(f'OK: {len(entries)} bilingual resources; XAML references and format arguments match.')
