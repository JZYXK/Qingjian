# 把清简发布到 GitHub：简版

这份文件是给发布者看的。普通使用者只需阅读《使用指南.md》。以下步骤都能在网页完成，不需要学习 Git 命令。

## 1. 准备文件

打开本次整理好的 `Qingjian-Publish-v0.5.4.3` 文件夹。发布时需要：

1. `Repository` 文件夹：里面是要上传到仓库的源代码。
2. 四个 ZIP 文件：Edge 插件、Windows 助手、清简源码、第三方源码。
3. `SHA256SUMS.txt`：下载文件的校验值。
4. `使用指南.md`：给使用者看的说明。

**只上传这里整理好的文件。不要上传正在运行的 Qingjian-V2、OfficePreview、local-checks、回退备份、测试报告、本地 AI 模型、个人配置或学习记录。手动清理清单.txt 仅供你清理电脑，不上传 GitHub。**

## 2. 新建或更新仓库，放源代码

已有仓库：直接打开原仓库，使用 Add file → Upload files，把 Repository 里面的内容按原路径更新，然后点击 Commit changes。此步骤不会自动更新 Releases 中的附件；还要完成第 3 节。

第一次发布：

1. 登录 GitHub，点击右上角“+”，选择 **New repository**。
2. 在名称中填 `qingjian`，选择 **Public**。
3. 不用再勾选自动添加 README、LICENSE 或 `.gitignore`，因为文件已经准备好了。
4. 点击 **Create repository**。
5. 在新仓库里，选择上传已有文件（通常是 **uploading an existing file**，以后也可点 **Add file → Upload files**）。
6. 打开本地 `Repository` 文件夹，把**里面的文件和文件夹**拖进上传区。不要只上传一个 Repository 文件夹，也不要只放一个源码 ZIP。
7. 等上传完成，在下方填写一句说明，例如“发布清简 0.5.4.3 源码”，点击 **Commit changes** 保存。

做到这里，仓库里应该能直接看到 README（首页就是使用指南）和 `app` 文件夹。

## 3. 创建下载页面，放安装包

1. 回到仓库首页，在右侧找到 **Releases**，点击 **Create a new release**。如果已经有版本，点击 **Draft a new release**。
2. 标签（Tag）填 `v0.5.4.3`，选择创建这个新标签。
3. 标题填“清简 0.5.4.3”。说明可以写：

   > 清简 0.5.4.3：回到已验证的稳定基线。支持 Edge 常见网页及主流 AI 网页输入框；桌面拼音转换兼容范围包括 Codex、Claude、记事本、Word 等。微信、Excel、PPT 尚不兼容可靠原位替换，可使用备用窗口。完整设置、隐私提醒及卸载步骤见使用指南。此版未上架 Edge 扩展商店。

4. 在附件区域上传这六个文件：
   - `Qingjian-Edge-v0.5.4.3.zip`
   - `Qingjian-Windows-x64-v0.5.4.3.zip`
   - `Qingjian-Source-v0.5.4.3.zip`
   - `Qingjian-ThirdPartySources-v0.5.4.3.zip`
   - `SHA256SUMS.txt`
   - `使用指南.md`
5. 等所有附件上传完成，检查文件名，点击 **Publish release**。

源码和第三方源码包也请一起上传，不要只发 exe 或两个用户安装包。版权声明和 LICENSE 请保持原样。

## 4. 发布后检查一次

1. 打开刚才的 Release 页面，确认六个附件都能看到。
2. 把这个 Release 页面的地址发给别人，不必发自己电脑上的目录。
3. 告诉普通使用者：只用网页下载 Edge 包；需要桌面功能再下载 Windows 包，先看使用指南。

原始 AI 模型约 2.5GB，没有放进发行包。用户需要时可用包内 `Download-AI.cmd` 主动下载，不要另外把模型塞进 GitHub 仓库。

## 5. 关于免费使用和商用

“免费使用，但禁止商用”的许可形式可以存在。只是清简当前使用的 GPL／AGPL 组件不允许给受覆盖部分另加这类限制。本版按已经确认的开源方案发布，不能再写“未经授权禁止商用／禁止修改”。

以后若想改成这种限制，需要先替换不兼容的组件，或取得相关权利人的额外授权；不能只改一个 LICENSE 文件。具体条款见 [GNU 许可常见问题](https://www.gnu.org/licenses/gpl-faq.en.html)。

图标说明：仓库 README 已包含桌面快捷方式和插件图标，上传 Repository 时请保留 guide-images 文件夹。Release 正文建议链接到仓库首页的图文使用指南；单独下载使用指南.md 时，图片文件夹也要放在同一级目录。Windows 下载包已内含完整图文指南。
