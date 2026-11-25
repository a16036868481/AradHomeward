# 局域网房间

两台电脑使用兼容的联机版本，并连到同一局域网。

1. 房主在启动器进入「多人模式 → 创建房间」。新建或选择本地账号后，点击「创建房间」。
2. 游戏启动后，房间窗口会显示地址和来宾人数。点击「复制邀请」，把邀请发给朋友。
3. 朋友进入「多人模式 → 加入房间」，粘贴邀请、填写玩家名称，再点「连接房间并进入游戏」。进入频道列表后，选择与房主相同的频道。

房主可以从修改器的「概览」重新打开房间窗口，也可以在开房前选择网卡。房主电脑保存来宾的角色和进度。同一位来宾在原电脑上用相同的玩家名称重新加入，可以继续该存档。每次重新开放房间会生成新的邀请。关闭房间或退出房主游戏会断开来宾连接。

来宾掉线后会看到「重新加入房间」，其中保留了原邀请和玩家名称。房间仍开放时可直接尝试重新加入。若房主已重新开房，粘贴新邀请后仍可使用原玩家名称。在角色选择界面继续上次保存的进度。

如果 Windows 弹出网络访问提示，允许此游戏联机组件在专用网络中通信。同一 Wi-Fi 下连不上时，确认路由器允许设备之间互访。

## 房主升级并保留存档

从 preview.36 升到 preview.37 时，房主升级即可。先正常结束旧版游戏并等备份完成，再把新版完整解压到新文件夹。

从旧版 `backups/` 下本次退出生成的备份中取出 `dnf_a12.sqlite`，复制到新版的 `DNF60_ACT12/ACT12_Server/`，替换新版附带的空存档。用旧存档开房并发送新邀请。来宾在原安装目录输入原玩家名，房主端会自动备份并修复受影响账号，然后载入原角色。

# LAN rooms

Use compatible LAN builds on both PCs and connect them to the same local network.

1. The host opens **Multiplayer**, then **Host a room**. Create or select a local account and choose **Host a room**.
2. Once the game starts, the room window shows its address and the number of connected guests. Choose **Copy invitation** and share it.
3. Guests open **Multiplayer**, then **Join a room**. Paste the invitation, enter a player name and select **Connect and play**. Select the same channel as the host.

The host can reopen the room window from the editor's **Overview** page or select a network adapter before creating a room. The host PC saves guest characters and progress. Returning guests can continue with the same player name on the same PC. Reopening the room generates a fresh invitation. Closing the room or exiting the host game disconnects guests.

If a guest loses the connection, **Rejoin room** opens with the previous invitation and player name. Use it to reconnect to a room that is still open. If the host has reopened the room, paste the new invitation and keep the same player name. Select your character to continue from the saved progress.

If Windows prompts for network access, allow the game's LAN component on private networks. If PCs on the same Wi-Fi cannot connect, check that the router permits device-to-device traffic.

## Upgrade a room host

For the upgrade from preview.36 to preview.37, update the host. Exit the old game normally and wait for its exit backup, then extract the new release into a new folder.

Copy `dnf_a12.sqlite` from the old installation's new exit backup under `backups/` into the new installation's `DNF60_ACT12/ACT12_Server/` and replace the blank bundled save. Host the room and share the new invitation. Guests can join from their existing installation with the same player name. The host backs up and repairs affected guest accounts when they rejoin, then loads their saved characters.
