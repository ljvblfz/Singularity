From: Mark Aiken
Sent: Wednesday, September 14, 2005 8:29 PM
Subject: MapPoint (aka Caffeinate Me!) Demo instructions

OK here’s a reprise of the demo instructions to make sure they’re up-to-date:

Build steps (things that are not built by a normal “msb Distro\BVT.proj” at the root)
- Build Applications\Network
- Build Applications\WebApps
- Build Applications\Cassini [after building Applications\Webapps]
- Build Applications\MapPointProxy
- Build Applications\SeattleTrafficProxy
- Don’t forget msb the distro again to recreate the ISO

Singularity machine setup:
- Be connected to the Internet.
- Mount the ISO image in the VPC cd-rom drive.

Singularity runtime steps, with direct Internet access:
  (type at the shell as shown)
- netstack &
- ipconfig /dev/nic0 dhcp start
  [You should see a success message giving Singularity’s IP address]
- mappoint &
- seattletraffic &
- cassini /app:MapDemoWebApp
  [You should see some startup spew from Cassini]

Singularity runtime step, with http proxy:
- netstack &
- ipconfig /dev/nic0 dhcp start
  [You should see a success message giving Singularity’s IP address]
- mappoint 157.54.58.20 &
  [You should replace with address of http proxy, if not on corpnet]
  [If the demo stalls after displaying "served /MapControl.js", it means
   the mappoint proxy can't reach mappoint servers (possibly because you
   have have the wrong http proxy.]
- seattletraffic &
- cassini /app:MapDemoWebApp [You should see some startup spew from Cassini]

Client-side steps:

- Open IE
- Make sure IE is configured correctly to reach the Internet;
  the VE demos rely on IE being able to fetch image segments from the
  Internet.
- Make sure IE is configured correctly to retrieve local content directly
  from the Singularity box.
- Go to http://SingularitysIPAddressHere/ve.aspx for the Virtual Earth demo.

Other notes:
- On some laptops with wireless, it work to boot with PXE assigned to the
  wireless interface. This may have to do with MAC address spoofing being
  unsupported over wireless. Use the wired connection instead.

Disclaimers:
- Responses from the MapPoint staging servers are sometimes up to 15 seconds
  or so in arriving. Obviously, this is unrelated to Singularity performance.
  This manifests as a very noticeable delay in rendering the coffee-demo maps.
- I’ve seen the demos fall over in GC stack walks and in the memory allocator,
  suggesting we still have heap-corruption issues. For best results, bring up
  the Singularity machine fresh and present the demo right away to minimize
  heap pressure.
- A good strategy is to start the VPC, then pause it (from the menu or by
  typing right-alt-P), then resume it as you're ready to show the demo (from
  the menu or by typing right-alt-P).
