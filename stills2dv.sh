#!/bin/sh

#Abort on error
set -e

echo "This script will download, compile and execute the stills2dv program written in C to generate reference images for comparison and testing"

rm -rf stills2dv

git clone https://github.com/buzzcard/stills2dv.git
cd stills2dv
make

echo "Generating example_output_jpg"
mkdir example_output_jpg
./stills2dv -tmpdir example_output_jpg exampleworkfile.s2d

echo "Generating example_output_ppm"
mkdir example_output_ppm
sed -e 's/type jpg/type ppm/g' exampleworkfile.s2d > exampleworkfile-ppm.s2d
./stills2dv -tmpdir example_output_ppm exampleworkfile-ppm.s2d
