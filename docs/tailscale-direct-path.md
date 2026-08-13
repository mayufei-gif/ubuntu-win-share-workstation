# Tailscale Direct Path Runbook

The SMB drive and the Tailscale transport are separate layers:

```text
Windows Z: -> SMB \\100.95.140.72\ubuntu-win -> Tailscale -> Ubuntu
```

The drive can appear as connected while Tailscale is still using DERP. A
customer acceptance requires both working SMB I/O and an observed direct path.

## 1. Confirm the LAN topology

Run on Ubuntu:

```bash
bash scripts/ubuntu/inspect-tailscale-direct.sh <NAS-LAN-IP>
```

Compare `LAN_CIDR`, the default gateway, and the route to the NAS. If Ubuntu
and the NAS already use the same LAN prefix, do not change the VM adapter just
to satisfy a diagram.

For this deployment, Ubuntu has previously reported `192.168.86.102` with
gateway `192.168.86.1`. The project configuration has used `192.168.86.103` as
the NAS LAN fallback. Reconfirm both addresses before treating this as proof
that bridge or equivalent LAN attachment is already active.

If Ubuntu is instead behind a hypervisor-only NAT subnet, change the VM to a
bridged or external virtual switch:

- VMware: Bridged networking on the physical LAN adapter.
- VirtualBox: Bridged Adapter on the physical LAN adapter.
- Hyper-V: External virtual switch.
- QEMU: a host bridge or TAP connected to the physical LAN.

Keep the old adapter definition as rollback evidence. Do not remove the NAT
adapter until SSH, DHCP, DNS, and the Samba share work through the bridged
address.

## 2. Check the actual UDP port

Do not assume that UDP 41641 is always the runtime port. The inspection script
checks Tailscale preferences and the active UDP listener. If neither confirms
a port, it reports the default as unconfirmed.

Check UFW without changing it:

```bash
sudo bash scripts/ubuntu/configure-tailscale-udp-firewall.sh --check
```

Apply the confirmed port:

```bash
sudo bash scripts/ubuntu/configure-tailscale-udp-firewall.sh --apply
```

Rollback:

```bash
sudo bash scripts/ubuntu/configure-tailscale-udp-firewall.sh --rollback
```

Set `TAILSCALE_PORT=<port>` only when the runtime port was independently
confirmed.

## 3. Router order

1. Prefer UPnP, NAT-PMP, or PCP if the router implements it reliably.
2. Re-run `tailscale netcheck` on Ubuntu and Windows.
3. Use a manual UDP forward only after confirming a stable public IPv4, the
   actual Tailscale UDP port, the Ubuntu LAN address, and the absence of CGNAT.
4. Prefer globally routable IPv6 when both networks and firewalls support it.

A manual forward must target Ubuntu, not the NAS, because the SMB endpoint is
the Ubuntu Tailscale node. Avoid forwarding TCP 445 from the public internet.

## 4. Acceptance

On Ubuntu:

```bash
tailscale netcheck
tailscale status
```

On Windows:

```powershell
.\scripts\windows\Test-TailscaleDirectPath.ps1 -RequireDirect
.\scripts\windows\Test-UbuntuWinShareSync.ps1 `
  -DriveLetter Z `
  -SshHost ubuntu-vm `
  -RemotePath /home/mana/C/ubuntu-win
```

Pass criteria:

- `tailscale ping` contains a peer IP and UDP port, not only `DERP(...)`.
- TCP 445 is reachable through the tailnet address.
- `Z:` maps to the expected UNC.
- Windows and Ubuntu hashes match for the same probe file.

Seeing `relay`, `DERP`, or only a drive icon is not a direct-path acceptance.

