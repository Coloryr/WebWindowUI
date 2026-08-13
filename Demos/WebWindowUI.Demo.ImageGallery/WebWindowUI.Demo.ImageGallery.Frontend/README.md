# WebWindowUI.Demo.ImageGallery.Frontend（纯 Vue 前端）

单窗口 main：双模式上传按钮 + 卡片/lightbox 列表查看。前端 WeakMap<item, blob URL> 缓存渲染（不把 url 存进条目对象，防深 watch 序列化回 .NET）。详见 [Demos/README.md](../../README.md)。
