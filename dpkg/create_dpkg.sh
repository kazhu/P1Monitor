#!/bin/bash
set -e

mkdir -p /tmp/p1monitor/usr/lib/p1monitor
cp ../P1Monitor /tmp/p1monitor/usr/lib/p1monitor
chmod +x /tmp/p1monitor/usr/lib/p1monitor/P1Monitor
cp ../createdump /tmp/p1monitor/usr/lib/p1monitor
chmod +x /tmp/p1monitor/usr/lib/p1monitor/createdump
cp ../*.dll /tmp/p1monitor/usr/lib/p1monitor
cp ../*.pdb /tmp/p1monitor/usr/lib/p1monitor
cp ../*.so /tmp/p1monitor/usr/lib/p1monitor
cp ../P1Monitor.*.json /tmp/p1monitor/usr/lib/p1monitor

mkdir -p /tmp/p1monitor/etc/p1monitor
cp ../appsettings.json /tmp/p1monitor/etc/p1monitor

mkdir -p /tmp/p1monitor/lib/systemd/system
cp p1monitor.service /tmp/p1monitor/lib/systemd/system

mkdir -p /tmp/p1monitor/DEBIAN
cp control /tmp/p1monitor/DEBIAN
cp postinst /tmp/p1monitor/DEBIAN
chmod +x /tmp/p1monitor/DEBIAN/postinst
cp prerm /tmp/p1monitor/DEBIAN
chmod +x /tmp/p1monitor/DEBIAN/prerm

dpkg-deb --build /tmp/p1monitor
mv /tmp/p1monitor.deb p1monitor.deb
rm -rf /tmp/p1monitor
