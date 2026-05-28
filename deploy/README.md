# `deploy/` — homelab deployment surface

The Compose stack that runs the ERP for Factory Games containers in production
lives in a sister repo: [`Homelab.Stacks.ErpForFactoryGames`](https://github.com/ChrisonSimtian/Homelab.Stacks.ErpForFactoryGames),
attached here as a submodule under [`Homelab.Stacks.ErpForFactoryGames/`](Homelab.Stacks.ErpForFactoryGames/).
See [ADR-0023](../docs/adr/0023-hosting-deployment-approach.md) for the
hosting decision and [`docs/operations/deploy.md`](../docs/operations/deploy.md)
for the tag-to-deploy runbook.

The two scripts in this directory bootstrap the Proxmox LXC that hosts the
stack. They live in *this* repo (not the sister) because they're independent
of the compose definition — same scripts apply if we ever swap the stack.

## Scripts

| Script | Purpose |
|---|---|
| `bootstrap-lxc.sh` | Idempotent. Installs Docker CE + compose plugin, creates an application user with passwordless sudo + docker group, copies root's `authorized_keys` to the user. Safe to re-run. |
| `harden-ssh.sh` | One-shot. Disables password auth + root-password login via a drop-in under `/etc/ssh/sshd_config.d/`. Run only after key auth is verified. |

## Fresh LXC, end-to-end

Assumes:
- Ubuntu 24.04 LTS LXC on Proxmox.
- **Unprivileged** with `nesting=1` and `keyctl=1` Features enabled
  (Proxmox UI → LXC → Options → Features).
- An SSH alias `erp-lxc` configured in `~/.ssh/config` on the dev box.
  This is also what `deploy/erp-deploy.json` references, so `./build.sh Up`
  and `./build.sh Provision` go through the same alias. A minimal stanza:

  ```
  Host erp-lxc
      HostName <your-lxc-ip>
      User chris
      IdentityFile ~/.ssh/id_ed25519
  ```

- Your SSH public key already pasted into `/root/.ssh/authorized_keys`
  on the LXC (one-time, via the Proxmox console).

Bootstrap:

```bash
# From the repo root on your dev box:
scp deploy/bootstrap-lxc.sh erp-lxc:/root/
scp deploy/harden-ssh.sh    erp-lxc:/root/

ssh erp-lxc 'bash /root/bootstrap-lxc.sh'

# Smoke: namespaces work?
ssh erp-lxc 'docker run --rm hello-world'

# Smoke: app user is wired up?
ssh erp-lxc 'sudo -u chris docker ps'

# Only once the above smoke is green:
ssh erp-lxc 'bash /root/harden-ssh.sh'
```

If `docker run hello-world` fails with `operation not permitted` or anything
mentioning user namespaces, that's the `nesting=1` Feature missing. Shut the
LXC down, tick `nesting` (and `keyctl`) in Proxmox, boot back up, re-run.

## Why not Ansible / cloud-init / something fancier?

For two scripts that touch one LXC, the overhead of a config-management
framework outweighs the value. If `deploy/` grows past ~3 hosts or starts
needing inventory, revisit.
