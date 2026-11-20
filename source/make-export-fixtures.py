"""Generate synthetic local export-test fixtures. No personal inputs required.
Copyright (c) 2026 Ibrahim Samir. SPDX-License-Identifier: GPL-3.0-only
"""
from pathlib import Path
import argparse, subprocess
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--output',type=Path,required=True)
parser.add_argument('--ffmpeg',type=Path,default=Path(__file__).resolve().parent.parent/'app/tools/ffmpeg.exe')
args=parser.parse_args();ffmpeg=args.ffmpeg.resolve(strict=True);out=args.output.resolve();out.mkdir(exist_ok=False)
def make(name,options):subprocess.run([str(ffmpeg),'-hide_banner','-loglevel','error','-n',*options,str(out/name)],check=True)
common=['-f','lavfi','-i','testsrc2=size=640x400:rate=30','-f','lavfi','-i','sine=frequency=440:sample_rate=48000']
make('normal.mp4',common+['-t','3','-c:v','libx264','-crf','18','-pix_fmt','yuv420p','-c:a','aac','-b:a','192k'])
make('vfr-delayed.mp4',['-f','lavfi','-i','testsrc2=size=640x400:rate=30','-itsoffset','.35','-f','lavfi','-i','sine=frequency=440:sample_rate=48000','-vf',"select='not(mod(n,3))+not(mod(n,7))'",'-fps_mode:v','vfr','-t','6','-c:v','libx264','-crf','18','-pix_fmt','yuv420p','-c:a','aac','-b:a','192k','-output_ts_offset','5'])
make('silent.mp4',['-f','lavfi','-i','testsrc2=size=640x400:rate=30','-t','2','-c:v','libx264','-crf','18','-pix_fmt','yuv420p'])
make('pcm.mkv',common+['-t','2','-c:v','libx264','-crf','18','-pix_fmt','yuv420p','-c:a','pcm_s16le'])
multi=common+['-f','lavfi','-i','sine=frequency=880:sample_rate=48000','-map','0:v:0','-map','1:a:0','-map','2:a:0','-t','2','-c:v','libx264','-crf','18','-pix_fmt','yuv420p']
make('multiple-audio.mp4',multi+['-c:a','aac'])
make('mixed-audio.mkv',multi+['-c:a:0','aac','-c:a:1','pcm_s16le'])
make('cancel.mp4',['-f','lavfi','-i','testsrc2=size=640x400:rate=30','-t','30','-c:v','libx264','-crf','25','-pix_fmt','yuv420p'])
make('short-audio.mp4',['-f','lavfi','-i','testsrc2=size=640x400:rate=30:duration=3','-f','lavfi','-i','sine=frequency=440:sample_rate=48000:duration=1','-c:v','libx264','-crf','18','-c:a','aac'])
make('marker.mkv',['-f','lavfi','-i',"color=black:size=640x400:rate=30,drawbox=color=white:t=fill:enable='between(t,1.5,1.7)'",'-f','lavfi','-i',"aevalsrc='if(between(t,1.5,1.7),0.6*sin(2*PI*440*t),0)':s=48000:d=3",'-t','3','-c:v','libx264','-crf','18','-c:a','pcm_s16le'])
print('Created export fixtures:',out)
