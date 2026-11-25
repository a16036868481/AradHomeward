# 局域网房间

两台电脑使用同一版本。同一地点可以用 Wi-Fi 或网线接入同一局域网。异地时，先用 Radmin VPN 或其他能让两台电脑通过 IPv4 互访的虚拟局域网软件加入同一个网络。

1. 房主进入「多人模式 → 创建房间」，新建或选中本地账号，再点「创建房间」。
2. 房间开放后点「复制邀请」，发给朋友。同一条邀请可以用于普通局域网和虚拟局域网。
3. 朋友进入「多人模式 → 加入房间」，粘贴完整邀请、填写玩家名称，再点「连接房间并进入游戏」。游戏会自动选择可用连接。进入频道列表后，双方选择同一个频道。

房主可从修改器的「概览」重新打开房间窗口。房主电脑保存来宾角色和进度。每次重新开放房间会生成新邀请。关闭房间或退出房主游戏会断开来宾。

掉线后，「重新加入房间」会保留上次的邀请和玩家名。房主已重新开房时，粘贴新邀请，再用原玩家名继续。

## 升级并保留存档

房主和来宾都把新版完整解压到各自的新文件夹。房主正常退出旧版游戏并等备份完成后，从旧版的 `backups/` 找到本次退出备份，将其中的 `dnf_a12.sqlite` 复制到新版的 `DNF60_ACT12/ACT12_Server/`，替换空存档。

来宾在自己的电脑上，从旧目录把 `setup/private/lan-player.key` 复制到新版对应位置。用原玩家名和房主的新邀请加入，即可继续原角色。

# LAN rooms

Use the same game version on both PCs. On the same site, connect to the same Wi-Fi or wired LAN. To play remotely, first join the same virtual LAN through Radmin VPN or another tool that gives both PCs direct IPv4 connectivity.

1. The host opens **Multiplayer → Host a room**, creates or selects an account, then chooses **Host a room**.
2. When the room opens, choose **Copy invitation** and share it. The same invitation works on a shared local or virtual LAN.
3. Guests open **Multiplayer → Join a room**, paste the full invitation, enter a player name and choose **Connect and play**. The game selects an available connection automatically. Choose the same channel in the game.

The host can reopen the room window from the trainer's **Overview** page. The host PC saves guest progress. Reopening the room creates a fresh invitation. Closing the room or exiting the host game disconnects guests.

After a disconnect, **Rejoin room** remembers the last invitation and player name. If the host reopened the room, paste the new invitation and keep the same player name.

## Upgrade and keep saves

Both players extract the new version to a new folder. On the host, exit the old game normally and let it finish the backup. Copy `dnf_a12.sqlite` from the old installation's exit backup in `backups/` to `DNF60_ACT12/ACT12_Server/` in the new installation, replacing its blank save.

On the guest PC, copy `setup/private/lan-player.key` from the guest's old folder to the corresponding place in their new folder. Reuse the same player name with the host's new invitation to load the existing character.
