# VirtualBox VM Setup Guide for WinVelo Integration Testing

This guide explains how to set up the Windows 11 VirtualBox virtual machine for autonomous real-mode integration testing.

---

## 1. Install VirtualBox

Open PowerShell on your host PC and run:
```powershell
winget install Oracle.VirtualBox --source winget
```
Restart your computer if prompted by VirtualBox.

---

## 2. Download Windows 11 ISO

Download the official Windows 11 multi-edition ISO directly from Microsoft:
[Download Windows 11 ISO](https://www.microsoft.com/software-download/windows11)

---

## 3. Create the Virtual Machine

Use the VirtualBox GUI or run these commands in PowerShell:

| Setting | Required Value |
|---|---|
| **VM Name** | `WinVelo-Test-VM` |
| **OS Type** | Windows 11 (64-bit) |
| **Base Memory (RAM)** | `4096 MB` (4 GB) |
| **Processors** | `2 CPUs` |
| **Virtual Disk Size** | `64 GB` (VDI format) |
| **Network** | NAT |

---

## 4. Install Windows 11 & Create Admin User

1. Boot the VM using the Windows 11 ISO.
2. Complete Windows Setup.
3. When prompted to create a user account, create a local administrator with:
   - **Username**: `winvelo-admin`
   - **Password**: `<choose-a-local-password>`
4. Verify that this user has Administrator rights.

---

## 5. Install VirtualBox Guest Additions

1. In the VirtualBox VM window menu, click **Devices** > **Insert Guest Additions CD image...**.
2. Inside the VM, open File Explorer, go to the CD drive, and run `VBoxWindowsAdditions.exe`.
3. Complete the setup and reboot the VM.
4. Guest Additions is required for automated file copying and test execution.

---

## 6. Take the "clean" Snapshot

1. Make sure Windows is fully booted and idle on the desktop.
2. Shut down the VM cleanly.
3. In VirtualBox Manager (or via PowerShell), take a snapshot named exactly `clean`:
   ```powershell
   & "C:\Program Files\Oracle\VirtualBox\VBoxManage.exe" snapshot "WinVelo-Test-VM" take "clean" --description "Base clean Windows 11 state"
   ```

---

## 7. Ready!

Once you complete these steps, tell Antigravity:
> **"VM is ready"**

Antigravity will then run `tests/vm/run-vm-tests.ps1` autonomously to verify real Windows system changes inside your VM.
