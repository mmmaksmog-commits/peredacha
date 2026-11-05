# Передача by mysik

Файлы, фото и тексты между компьютером и телефоном за секунды — без проводов, Telegram, облаков и регистрации. Работает по Wi-Fi, даже если интернета нет.

**Страница программы и скачивание:** https://mysik-portfolio.vercel.app/soft/peredacha.html

## Что умеет

- **Вход по QR-коду** — навели камеру — и всё. На телефон ничего ставить не нужно, работает в браузере iPhone и Android.
- **С телефона на ПК** — фото и файлы сохраняются в «Загрузки\Передача by mysik».
- **С ПК на телефон** — перетащите файлы в окно, на телефоне нажмите «Скачать».
- **Тексты и ссылки** — отправили с телефона — на компьютере текст уже в буфере обмена. И обратно.
- **Без интернета** — нужна только общая Wi-Fi сеть.
- **Безопасно** — в QR-коде секретный ключ, без него не подключиться. Новый ключ при каждом запуске.

## Как начать

Нужна только Windows 10 или 11 — компилятор .NET Framework 4.x уже встроен в систему.

1. Скачайте репозиторий: зелёная кнопка **Code → Download ZIP**, и распакуйте.
2. Запустите `build.bat` — через пару секунд рядом появится `Peredacha.exe`.
3. Откройте `Peredacha.exe`. Права администратора не нужны.
4. **Разрешите доступ к сети.** При первом запуске Windows спросит — отметьте «Частные сети».
5. **Отсканируйте QR-код телефоном.** Откройте ссылку и передавайте файлы в обе стороны.

## Полезно знать

- Не подключается — проверьте, что телефон в той же Wi-Fi сети, а не на мобильном интернете, и что в роутере не включена «изоляция клиентов» (бывает в гостевых сетях).

Файлы: `MainWindow.xaml`, `Peredacha.cs`, `QrCode.cs`, `README.txt`, `Shell.cs`, `Theme.xaml`, `app.ico`, `app.manifest`, `build.bat`, `phone.html`.

## Другие программы by mysik

- [Zapret GUI](https://mysik-zapret.netlify.app) — Возвращает Discord, YouTube и Steam одной кнопкой. Без VPN: скорость и IP остаются вашими.
- [DNS Switch](https://mysik-portfolio.vercel.app/soft/dns-switch.html) — Меняет DNS в один клик и сам проверяет, какой быстрее именно у вас.
- [Пинг](https://mysik-portfolio.vercel.app/soft/ping.html) — Следит за интернетом каждую секунду и подсказывает, где проблема: Wi-Fi, роутер или провайдер.
- [Video Loader](https://mysik-portfolio.vercel.app/soft/video-loader.html) — Скачивает видео и музыку с YouTube, VK Видео, RuTube и TikTok: до 4K, плейлисты, MP3 с обложкой.
- [Converter](https://mysik-portfolio.vercel.app/soft/converter.html) — Видео, музыка и картинки в нужный формат, включая HEIC с iPhone. Умеет сжимать перед отправкой.
- [Снимок](https://mysik-portfolio.vercel.app/soft/snimok.html) — Скриншот области по PrintScreen, стрелки, размытие и нумерация. Сразу в буфер.
- [Win Cleaner](https://mysik-portfolio.vercel.app/soft/win-cleaner.html) — Чистит мусор, убирает рекламу и Copilot, наводит порядок в автозагрузке. Всё можно вернуть.
- [Диск](https://mysik-portfolio.vercel.app/soft/disk.html) — Карта диска за минуту: видно, какие папки и файлы съели всё место.
- [Раскладка](https://mysik-portfolio.vercel.app/soft/raskladka.html) — Напечатали «ghbdtn»? Одна клавиша — и слово исправлено, раскладка переключена.
- [Буфер](https://mysik-portfolio.vercel.app/soft/bufer.html) — Помнит всё, что вы копировали: текст, ссылки, картинки. Ctrl+Shift+V — и вставлено.
- [Визитка](https://mysik-portfolio.vercel.app/soft/vizitka.html) — Конструктор сайтов для мастеров: маникюр, барбер, массаж. Сайт под ключ за 15–30 минут.

Все программы — в портфолио: https://mysik-portfolio.vercel.app/#programs  
Сделано [mysik](https://mysik-portfolio.vercel.app).
