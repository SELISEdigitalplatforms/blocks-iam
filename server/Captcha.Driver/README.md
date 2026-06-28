# SeliseBlocks.CaptchaDriver

Captcha driver for SELISE Blocks. Provides a thin `ICaptchaDriverService` over the
captcha domain services (create, submit, and verify captcha challenges).

## Usage

Register the captcha services in your DI container:

```csharp
using Blocks.Extension.DependencyInjection;

services.RegisterBlocksCaptchaService();
```

Then inject `ICaptchaDriverService` (or `ICaptchaService`) where you need captcha
functionality.

## Configuration

Captcha settings are read from the shared `Secrets` collection, using the document
whose `SecretKey` is `captcha`. The values live under `KeyPairs` (key lookup is
case-insensitive):

| Key | Description |
| --- | --- |
| `IsEnable` | Whether captcha is enabled (`true`/`false`, case-insensitive). |
| `Provider` | Captcha provider: `recaptcha`, `hcaptcha`, or `bcaptcha`. |
| `CaptchaKey` | Provider site/public key. |
| `CaptchaSecret` | Provider secret key. |
| `CaptchaProvider` | Generator implementation (e.g. `EasyCaptchaGenerator`). |

> The legacy layout — a `KeyValuePairs` dictionary with camelCase keys
> (`isEnable`, `provider`, `captchaKey`, `captchaSecret`, `captchaGenerator`) — is
> also still accepted.
