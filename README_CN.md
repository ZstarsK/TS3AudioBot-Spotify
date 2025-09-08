# TS3AudioBot - 中文版说明文档

这是一个开源的TeamSpeak3音乐机器人，可以播放音乐并提供更多功能。

- **有问题？** 查看我们的 [Wiki](https://github.com/Splamy/TS3AudioBot/wiki)、[FAQ](https://github.com/Splamy/TS3AudioBot/wiki/FAQ)，或在 [Gitter聊天室](https://gitter.im/TS3AudioBot/Lobby?utm_source=share-link&utm_medium=link&utm_campaign=share-link) 提问
- **遇到问题或复杂情况？** [提交问题](https://github.com/Splamy/TS3AudioBot/issues/new/choose)
  - 请使用我们提供的模板并填写相关信息，除非不适用或有充分理由不填写
  - 这有助于我们更快地处理技术问题
  - 请使用英文提交问题，这样便于大家参与并保持问题的相关性
- **想要支持这个项目？**
  - 可以讨论和建议功能。不过[待办事项](https://github.com/Splamy/TS3AudioBot/projects/2)很多，功能请求可能需要时间
  - 可以贡献代码。这总是受欢迎的，请在开始前先提交问题或联系维护者讨论
  - 可以在 [Patreon](https://patreon.com/Splamy) 或 [PayPal](https://paypal.me/Splamy) 支持我

## 功能特性
* 播放YouTube和SoundCloud歌曲以及Twitch直播（可通过插件扩展）
* 歌曲历史记录
* 多种语音订阅模式；包括对客户端、频道和私聊群组
* 所有用户的播放列表管理
* 强大的权限配置
* 插件支持
* Web API
* 多实例支持
* 本地化支持
* 低CPU和内存占用，使用我们自写的无头ts3客户端

要查看计划中和进行中的功能，请查看我们的[路线图](https://github.com/Splamy/TS3AudioBot/projects/2)。

## 机器人命令
机器人完全可以通过聊天操作。  
要开始使用，请向机器人发送 `!help`。  
要查看所有命令，请查看我们的实时 [OpenApiV3生成器](http://tab.splamy.de/openapi/index.html)。  
要深入了解命令教程，请查看 [Wiki中的这里](https://github.com/Splamy/TS3AudioBot/wiki/CommandSystem)。

## 安装部署

### 下载
选择并下载适合您平台和偏好的构建版本：

| 平台 | 稳定版 | 实验版 |
|------|--------|--------|
| 说明 | 版本通常被认为是稳定的，但不会快速获得更大的功能 | 总是有最新最好的功能，但可能不够稳定或有功能损坏 |
| Windows_x64 | [![下载](https://img.shields.io/badge/下载-master-green.svg)](https://splamy.de/api/nightly/ts3ab/master_win_x64/download) | [![下载](https://img.shields.io/badge/下载-develop-green.svg)](https://splamy.de/api/nightly/ts3ab/develop_win_x64/download) |
| Linux_x64 | [![下载](https://img.shields.io/badge/下载-master-green.svg)](https://splamy.de/api/nightly/ts3ab/master_linux_x64/download) | [![下载](https://img.shields.io/badge/下载-develop-green.svg)](https://splamy.de/api/nightly/ts3ab/develop_linux_x64/download) |
| Docker | [![Docker](https://img.shields.io/badge/Docker-0.11.0-0db7ed.svg)](https://github.com/getdrunkonmovies-com/TS3AudioBot_docker) (注意：此构建由社区维护，包含所有依赖项以及预配置的youtube-dl) | - |

（我们在[夜间构建服务器](https://splamy.de/Nightly#ts3ab)上提供更多构建版本，如linux arm/arm64和.NET framework依赖构建）

### 系统依赖安装

#### Linux系统
安装必需的依赖项：
* **Ubuntu/Debian系统**：  
  运行 `sudo apt-get install libopus-dev ffmpeg`
* **Arch Linux系统**：  
  运行 `sudo pacman -S opus ffmpeg`
* **CentOS 7系统**：  
  运行
    ```bash
    sudo yum -y install epel-release
    sudo rpm -Uvh http://li.nux.ro/download/nux/dextop/el7/x86_64/nux-dextop-release-0-5.el7.nux.noarch.rpm
    sudo yum -y install ffmpeg opus-devel
    ```
* **手动安装**：
    1. 确保您已安装C编译器
    2. 使用 `chmod u+x InstallOpus.sh` 使Opus脚本可执行，然后运行 `./InstallOpus.sh`
    3. 获取ffmpeg [32位](https://johnvansickle.com/ffmpeg/builds/ffmpeg-git-i686-static.tar.xz) 或 [64位](https://johnvansickle.com/ffmpeg/builds/ffmpeg-git-amd64-static.tar.xz) 二进制文件
    4. 使用 `tar -vxf ffmpeg-git-*XXbit*-static.tar.xz` 解压ffmpeg压缩包
    5. 从 `ffmpeg-git-*DATE*-amd64-static/ffmpeg` 获取ffmpeg二进制文件并复制到您的TS3AudioBot文件夹中

#### Windows系统
1. 获取ffmpeg [32位](https://ffmpeg.zeranoe.com/builds/win32/static/ffmpeg-latest-win32-static.zip) 或 [64位](https://ffmpeg.zeranoe.com/builds/win64/static/ffmpeg-latest-win64-static.zip) 二进制文件
2. 打开压缩包，将 `ffmpeg-latest-winXX-static/bin/ffmpeg.exe` 中的ffmpeg二进制文件复制到您的TS3AudioBot文件夹中

### 可选依赖
如果机器人无法播放某些YouTube视频，可能是由于一些嵌入限制阻止了播放。  
您可以安装 [youtube-dl](https://github.com/rg3/youtube-dl/) 二进制文件或源代码文件夹（并在配置中指定路径）来尝试绕过此限制。

### 首次设置
1. 运行机器人：`./TS3AudioBot`（Linux）或 `TS3AudioBot.exe`（Windows），然后按照设置说明操作
2. （可选）关闭机器人并配置您的 `rights.toml` 文件以满足您的需求
   您可以使用自动生成文件中建议的模板规则，
   或深入了解权限语法 [这里](https://github.com/Splamy/TS3AudioBot/wiki/Rights)。
   然后重新启动机器人
3. （可选，但强烈建议确保一切正常工作）
   - 为ServerAdmin组（或具有等效权限的组）创建特权密钥
   - 向机器人发送私聊消息 `!bot setup <特权密钥>`
4. 恭喜，您完成了！享受您喜爱的音乐，尝试疯狂的命令系统或做任何您想做的事情 ;)  
   要进一步阅读，请查看 [命令系统](https://github.com/Splamy/TS3AudioBot/wiki/CommandSystem)

## 手动构建

|master|develop|
|:--:|:--:|
|[![构建状态](https://ci.appveyor.com/api/projects/status/i7nrhqkbntdhwpxp/branch/master?svg=true)](https://ci.appveyor.com/project/Splamy/ts3audiobot/branch/master)|[![构建状态](https://ci.appveyor.com/api/projects/status/i7nrhqkbntdhwpxp/branch/develop?svg=true)](https://ci.appveyor.com/project/Splamy/ts3audiobot/branch/develop)|

### 下载源码
使用 `git clone --recurse-submodules https://github.com/Splamy/TS3AudioBot.git` 下载git仓库。

#### Linux构建
1. 按照[此教程](https://docs.microsoft.com/dotnet/core/install/linux-package-managers)获取最新的 `dotnet core 3.1` 版本并选择您的平台
2. 使用 `cd TS3AudioBot` 进入仓库目录
3. 执行 `dotnet build --framework netcoreapp3.1 --configuration Release TS3AudioBot` 构建AudioBot
4. 二进制文件将在 `./TS3AudioBot/bin/Release/netcoreapp3.1` 中，可以使用 `dotnet TS3AudioBot.dll` 运行

#### Windows构建
1. 确保您已安装带有 `dotnet core 3.1` 开发工具链的 `Visual Studio`
2. 使用Visual Studio构建AudioBot

### 构建Web界面
1. 使用您选择的控制台进入 `./WebInterface` 文件夹
2. 运行 `npm install` 恢复或更新此项目的所有依赖项
3. 运行 `npm run build` 构建项目  
   构建的项目将在 `./WebInterface/dist` 中  
   确保在ts3audiobot.toml中将webinterface路径设置为此文件夹
4. 您也可以使用 `npm run start` 进行开发  
   这将使用webpack开发服务器和实时重载，而不是ts3ab服务器

## 社区

### 本地化
:speech_balloon: *想要帮助翻译或改进翻译？*  
加入我们在 [Transifex](https://www.transifex.com/respeak/ts3audiobot/) 上的翻译工作  
或在我们的 [Gitter](https://gitter.im/TS3AudioBot/Lobby?utm_source=share-link&utm_medium=link&utm_campaign=share-link) 上讨论或询问任何问题！  
所有帮助都受到赞赏 :heart:

翻译需要手动批准，然后会自动构建并部署到[我们的夜间服务器这里](https://splamy.de/TS3AudioBot)。

## 许可证
此项目在 [OSL-3.0](https://opensource.org/licenses/OSL-3.0) 许可证下发布。

为什么选择OSL-3.0：
- OSL允许您链接到我们的库而无需披露您自己的项目，如果您想将TSLib作为库使用，这可能很有用
- 如果您创建插件，您不必像GPL那样公开它们（尽管我们很高兴如果您分享它们 :)
- 通过OSL，我们希望允许您提供TS3AB作为服务（甚至商业用途）。我们不希望软件被出售，而是服务。我们希望这个软件对每个人都是免费的
- 太长不看？ https://tldrlegal.com/license/open-software-licence-3.0

---
[![forthebadge](http://forthebadge.com/images/badges/60-percent-of-the-time-works-every-time.svg)](http://forthebadge.com) [![forthebadge](http://forthebadge.com/images/badges/built-by-developers.svg)](http://forthebadge.com) [![forthebadge](http://forthebadge.com/images/badges/built-with-love.svg)](http://forthebadge.com) [![forthebadge](http://forthebadge.com/images/badges/contains-cat-gifs.svg)](http://forthebadge.com) [![forthebadge](http://forthebadge.com/images/badges/made-with-c-sharp.svg)](http://forthebadge.com)

## 快速部署指南

### 1. 系统要求
- **操作系统**: Windows 10/11, Linux (Ubuntu 18.04+, CentOS 7+), macOS
- **内存**: 最少512MB RAM
- **磁盘空间**: 至少100MB可用空间
- **网络**: 稳定的互联网连接

### 2. 快速开始（推荐）

#### Windows用户
1. **下载**: 从上面的链接下载Windows x64稳定版
2. **解压**: 解压到任意文件夹（如 `C:\TS3AudioBot\`）
3. **安装FFmpeg**: 
   - 下载FFmpeg 64位版本
   - 解压后将 `ffmpeg.exe` 复制到TS3AudioBot文件夹
4. **运行**: 双击 `TS3AudioBot.exe`
5. **配置**: 按照屏幕提示输入TeamSpeak服务器信息

#### Linux用户
1. **安装依赖**:
   ```bash
   # Ubuntu/Debian
   sudo apt-get update
   sudo apt-get install libopus-dev ffmpeg
   
   # CentOS/RHEL
   sudo yum install epel-release
   sudo yum install ffmpeg opus-devel
   ```

2. **下载并运行**:
   ```bash
   # 下载最新版本
   wget https://splamy.de/api/nightly/ts3ab/master_linux_x64/download -O ts3ab.tar.gz
   tar -xzf ts3ab.tar.gz
   cd TS3AudioBot
   
   # 运行
   ./TS3AudioBot
   ```

### 3. 基本配置

#### 首次运行配置
1. **服务器连接**:
   - 输入TeamSpeak服务器地址
   - 输入服务器端口（默认9987）
   - 输入服务器密码（如果有）

2. **机器人设置**:
   - 输入机器人昵称
   - 选择默认频道
   - 设置管理员权限

3. **权限配置**:
   - 编辑 `rights.toml` 文件
   - 配置用户权限和命令访问

### 4. 常用命令

#### 基本音乐命令
- `!play <歌曲名或链接>` - 播放音乐
- `!pause` - 暂停播放
- `!resume` - 恢复播放
- `!stop` - 停止播放
- `!next` - 下一首
- `!previous` - 上一首
- `!volume <0-100>` - 设置音量

#### 播放列表命令
- `!playlist add <歌曲>` - 添加到播放列表
- `!playlist list` - 显示播放列表
- `!playlist clear` - 清空播放列表
- `!playlist shuffle` - 随机播放

#### 管理命令
- `!help` - 显示帮助
- `!info` - 显示当前播放信息
- `!history` - 显示播放历史
- `!subscribe` - 订阅机器人（私聊模式）

### 5. 高级配置

#### 配置文件说明
- `ts3audiobot.toml` - 主配置文件
- `rights.toml` - 权限配置文件
- `bots/` - 机器人实例配置文件夹

#### 多机器人实例
1. 在 `bots/` 文件夹中创建新的配置文件
2. 为每个机器人设置不同的配置
3. 使用 `!bot create <名称>` 创建新实例

#### 插件安装
1. 将插件文件放入 `plugins/` 文件夹
2. 在配置文件中启用插件
3. 重启机器人

### 6. 故障排除

#### 常见问题
1. **无法连接服务器**:
   - 检查服务器地址和端口
   - 确认防火墙设置
   - 验证服务器密码

2. **无法播放音乐**:
   - 检查FFmpeg是否正确安装
   - 验证网络连接
   - 查看日志文件

3. **权限问题**:
   - 检查 `rights.toml` 配置
   - 确认用户组权限
   - 验证命令权限设置

#### 日志查看
- Windows: 查看控制台输出
- Linux: 使用 `journalctl -u ts3audiobot` 或查看日志文件

### 7. 性能优化

#### 系统优化
- 调整音频比特率（8-98 kbps）
- 配置合适的缓冲区大小
- 限制并发连接数

#### 网络优化
- 使用CDN加速
- 配置代理设置
- 优化网络延迟

### 8. 安全建议

#### 基本安全
- 定期更新机器人版本
- 使用强密码
- 限制管理员权限

#### 网络安全
- 配置防火墙规则
- 使用VPN（如需要）
- 监控异常活动

---

**注意**: 此文档基于TS3AudioBot的官方文档翻译，如有疑问请参考[原始英文文档](README.md)或访问[官方GitHub仓库](https://github.com/Splamy/TS3AudioBot)。
