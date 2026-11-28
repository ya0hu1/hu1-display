# Third-party notices / 第三方来源

The 晖映 / hu1 display Windows workspace, controller and original icon are covered by the root [MIT license](LICENSE). This project is not an official AnyDisplay, DeskMate, Google or Virtual Display Driver distribution.

- **AnyDisplay** — upstream H.264 video engine and Android client by Yahya Efe Kuruçay, MIT. The original client retains its upstream identity. The modified hu1 Android receiver in `android-client/` uses a separate application ID and retains the [upstream MIT license](android-client/UPSTREAM_LICENSE). [Pinned v0.1.4 source tree](https://github.com/efekurucay/anydisplay/tree/01eb3eb0f7481655f2e42a8df9288b21d7c9c9b0), [upstream license](protocol/UPSTREAM_LICENSE), [source metadata](UPSTREAM.json).
- **Android SDK Platform-Tools** — ADB and its companion libraries come from [Google](https://developer.android.com/tools/releases/platform-tools) under their respective terms and notices. No Google binaries are distributed in this source preview.
- **Virtual Display Driver / MttVDD** — extend mode uses an existing non-primary virtual display. See the [upstream driver project](https://github.com/VirtualDrivers/Virtual-Display-Driver) for its own terms. No driver binary is distributed or automatically installed by this source preview.

自有控制层使用 MIT；不把第三方引擎、APK、ADB 或驱动声明为原创，也不使用 DeskMate 的代码、图标或品牌。源码包含保留 MIT 许可的 Android 衍生客户端，不分发上述组件的二进制或本机私有验证资料。若另行打包，须独立核对相应许可、NOTICE、版本及校验值。
