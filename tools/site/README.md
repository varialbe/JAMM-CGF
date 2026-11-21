# Static site automation

`auto-publish.sh` fetches repository refs, creates a temporary isolated clone checked out at `origin/main`, regenerates the static pages there, and syncs the site only when generated files differ from the checked-in output. Local branches and uncommitted work are never included. The publish uses the `varialbe@varialbe.xyz:/var/www/html/cgf-mods/` SSH target and preserves the generator scripts and source directory on the server.

The user service and timer units are templates. Before installing them, edit the service `ExecStart` if the repository is not at `~/JAMM-CGF`. To install later:

```sh
mkdir -p ~/.config/systemd/user
cp tools/site/jamm-cgf-site-publish.{service,timer} ~/.config/systemd/user/
systemctl --user daemon-reload
systemctl --user enable --now jamm-cgf-site-publish.timer
```

The timer starts five minutes after login/boot and then runs every 30 minutes. The service requires `git`, `python3`, `rsync`, and SSH access for the configured publish account. The timer is not enabled as part of this repository change.
