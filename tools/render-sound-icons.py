#!/usr/bin/env python3
"""Render the pinned, unmodified Lucide paths at 4x size with macOS ImageIO."""
from pathlib import Path
import subprocess
import tempfile
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parent.parent / 'Assets/Lucide'
for name in ('volume-2', 'volume-off'):
    svg = ET.parse(root / (name + '.svg'))
    svg.getroot().set('width', '96')
    svg.getroot().set('height', '96')
    with tempfile.TemporaryDirectory(prefix='bricklasare-lucide-') as directory:
        source = Path(directory) / (name + '.svg')
        svg.write(source, encoding='utf-8', xml_declaration=True)
        subprocess.run(['/usr/bin/sips', '-s', 'format', 'png', str(source),
                        '--out', str(root / (name + '.png'))], check=True)
