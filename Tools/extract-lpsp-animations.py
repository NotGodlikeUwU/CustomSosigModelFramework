"""List or extract specifically named FBX assets from the user's Unity package."""
import argparse
import gzip
import pathlib
import re
import tarfile

parser = argparse.ArgumentParser()
parser.add_argument('package')
parser.add_argument('--output', default='animations/lpsp')
parser.add_argument('--names', nargs='*', default=[])
args = parser.parse_args()
selected = {}
with gzip.open(args.package, 'rb') as stream, tarfile.open(fileobj=stream, mode='r|') as archive:
    for member in archive:
        if member.name.endswith('/pathname'):
            name = archive.extractfile(member).read().decode('utf-8').strip('\0\r\n ')
            name = re.sub(r'[\r\n]+00\s*$', '', name)
            if name.endswith('.fbx') and ('A_TP_CH_' in name or 'SK_TP_CH_' in name):
                if not args.names:
                    print(name)
                elif pathlib.PurePosixPath(name).name in args.names:
                    selected[member.name.rsplit('/', 1)[0] + '/asset'] = pathlib.PurePosixPath(name).name
if args.names:
    output = pathlib.Path(args.output)
    output.mkdir(parents=True, exist_ok=True)
    with gzip.open(args.package, 'rb') as stream, tarfile.open(fileobj=stream, mode='r|') as archive:
        for member in archive:
            if member.name in selected:
                destination = output / selected.pop(member.name)
                destination.write_bytes(archive.extractfile(member).read())
                print(destination)
    if selected:
        raise RuntimeError(f'Missing package assets: {selected}')
