# Custom Sosig Replacer for H3VR

BepInEx/Harmony-мод для H3VR, заменяющий визуальную модель каждого создаваемого Sosig на Metrocop с пользовательскими Mixamo-анимациями.

## Текущее состояние

Версия 3.5.0: UV глаза лежат в повторяемом тайле (>1), поэтому исправлен Clamp карты эмиссии на Repeat. Дополнительно 161 треугольник линз выделен в отдельный Unlit/Color submesh, чтобы синий цвет был виден без света и независимо от Standard emission variant. Ragdoll-суставы теперь создаются непосредственно в позе смерти, без старой spawn-reference и агрессивной projection. После смерти части переходят на Default слой; не соседние части тела сталкиваются друг с другом, соседние joint-пары исключены для устранения исходного пересечения в плечах/тазу. Начальные скорости очищены, depenetration ограничена, итерации solver повышены. Бэкап установленного пакета 3.4.0: `backups/v3.4.0-installed-before-ragdoll`.

Компиляция и выбор треугольников глаз проверены; отсутствие подскока и стабильность самоколлизий требуется проверить в игре.

Версия 3.4.0: исправлено наложение atlas-текстур. Внешние PNG являются вертикальной инверсией embedded-изображений GLB, поэтому для этих PNG экспорт сохраняет исходный V без дополнительного `1-V`. На всех 20 685 UV-парах это проверено автоматически. Добавлены tangent-векторы и alpha=X упаковка обычных PNG normal maps для desktop Standard shader. Линзы глаз получили локальную синюю эмиссию, без сценовых источников света. Настройки: `Visual.BlueEyeEmission = true`, `Visual.EyeEmissionIntensity = 3`; ореол зависит от bloom камеры. Анимации не изменены. Бэкап: `backups/v3.3.0-before-uv-eyes`.

Версия 3.3.0: итоговая поза стоящей/присевшей модели корректируется по четырём реальным skinned-точкам подошв после анимации и выравнивания таза. Опора проверяется под каждой точкой; коллайдеры Sosig исключены, поиск начинается ниже прежнего высокого луча, чтобы не выбирать перекрытия над головой. При Ballistic остаётся только защита подошв от проникновения, без притягивания к полу; ragdoll не изменён. `X Bot@Knocked Down.fbx` добавлен как второй вариант падения: варианты чередуются по эпизодам, один выбор сохраняется на весь эпизод. Экспорт падений держит настоящий последний кадр, не зацикливая клип.

Бэкап 3.2.0: `backups/v3.2.0-before-sole-grounding`. Геометрические тесты подтверждают расчёты подошв, но выбор поверхностей и визуальный результат требуют повторной проверки в игре.

Версия 3.2.0: после анализа записи `video_2026-09-27_23-22-05.mp4` добавлены движение оружейного верха тела из Low Poly Shooter Pack, шесть согласованных направлений locomotion, дыхание, добавочная отдача и переходы поз за 0.18 секунды. Скорость шага берётся из NavMeshAgent (при его отсутствии — из перемещения корня), не из качающегося physical link. Шаг запускается при 0.18 м/с и останавливается ниже 0.08 м/с; фаза зависит от пройденного расстояния. Поддерживающая рука следует alternate grip оружия, если он есть, иначе — откалиброванной позиции в пространстве оружия; при reload и dual wield эта коррекция отпускается. Для ножей огнестрельный hand-driver не применяется.

Подтверждены компиляция и автономные тесты, но реалистичность этой версии в VR ещё требует проверки. Бэкап 3.1.0: `backups/v3.1.0-before-locomotion-blend`.

- Работает глобально во всех режимах и со всеми TNH-персонажами; отдельная категория и отдельный TNH-персонаж больше не создаются.
- Не клонирует и не меняет `SosigEnemyTemplate`: AI, IFF, скорость, штатное здоровье/link integrity, оружие, броня и параметры сложности остаются от фактически заспавненного H3VR Sosig.
- Использует `metrocop/source/remade metro cop HL2.glb`: 55-bone ValveBiped rig, два skinned mesh primitive, UV, body/mask textures и normal maps.
- Сохраняет оружие Sosig видимым.
- Создаёт proxy вне иерархии Sosig, следует за его горизонтальным положением и направлением, а высоту живой модели фиксирует по поверхности пола, чтобы физические колебания links не заставляли модель прыгать вверх-вниз.
- Использует все пользовательские Mixamo FBX из `animations`: walking, backward run, медленные/быстрые strafe left/right, hit reaction и death.
- Версия 3.0 использует отдельный `SosigAnimationDirector`: он читает штатные `BodyState`, `BodyPose`, `SosigHand.Pose`, `SosigWeapon.UsageState`, тип боеприпаса и `TimeSinceFired()`.
- Использует полный authored body pose: направленные Mixamo-клипы управляют всем телом во время locomotion, а third-person клипы Low Poly Shooter Pack поверх них задают естественную позу верхней части тела для rifle/handgun idle, aim, fire и reload.
- Пока персонаж контролирует себя, физические links Sosig не переписывают позвоночник, голову и руки модели. Модель самостоятельно и с ограниченной угловой скоростью поворачивается по направлению цели или движения, не по направлению управляемого ею оружия. Links снова получают приоритет только в состоянии `Ballistic` и при ragdoll.
- Направление weapon binding инвертировано относительно старых версий: authored hand bones модели теперь двигают штатные `SosigHand.Target`, поэтому настоящее оружие Sosig следует анимированным человеческим рукам. Live IK от Sosig к модели полностью отключён.
- Выбирает locomotion по направлению фактической скорости Sosig и проигрывает hit/death как однократные состояния.
- Уже при скорости выше 0,35 м/с Mixamo locomotion ног получает полный вес; authored-движение рук больше не конфликтует с оружием.
- Аналитический двухзвенный IK больше не строит позу руки с нуля: он применяется после authored weapon pose только как небольшая финальная коррекция к реальному `SosigHand.Target`. Во время reload его вес почти полностью снимается.
- Переходы между relaxed/weapon/aim/reload сглаживаются независимо. Fire-клип запускается по реальному сбросу `SosigWeapon.TimeSinceFired()` и может корректно перезапускаться для автоматического огня.
- Анатомический swing/twist limiter оставлен только как слабая страховка после authored animation и IK, чтобы не разрушать валидные оружейные позы.
- Голова, шея и позвоночник следуют физическим head/torso links Sosig и имеют приоритет над Mixamo.
- `Sosig.BodyPose` выбирает crouch idle/forward-left/forward-right/left/backward. Переходы Standing→Crouching и Crouching→Standing используют отдельные одноразовые Mixamo-клипы; при быстром развороте переход продолжается с зеркальной позиции времени без скачка к первому кадру.
- В Standing модель остаётся привязанной к полу, а в Crouching/Prone её вертикальная координата следует физическому `UpperLink` Sosig, чтобы анимация приседа не висела над хитбоксами.
- `Ballistic` запускает один защёлкнутый falling-эпизод, который не перезапускается из-за промежуточных изменений `BodyPose` или краткого восстановления контроля.
- Во время падения таз модели совмещается с физическим `UpperLink`; поверхность только предотвращает проникновение ступней в пол и больше не удерживает падающую модель в воздухе.
- Прямо отключает `Sosig.Renderers`, `Sosig.Meshes` и `SosigLink.SubRenderer`, поэтому штатное тело не должно оставаться под Metrocop.
- Создаёт 11 физических частей по схеме COD Zombies: Head, Chest, Hips, верхние/нижние руки и ноги. На каждой части находятся Collider, Rigidbody, PMat-копия и `IFVRDamageable`.
- Голова использует множитель урона x3, грудь x2, остальные части x1. Урон передаётся соответствующему штатному `SosigLink`, поэтому здоровье и смерть остаются авторитетными для H3VR.
- Пока Sosig жив, физические части кинематические и следуют анимированным костям. После смерти ручная анимация прекращается, части становятся динамическими и соединяются `CharacterJoint`, а skinned mesh следует ragdoll.

GLB содержит полноценный rig и skin weights, но не содержит собственных animation clips (`animations: []`). Версия 3.0 не использует анимации COD Zombies, `AvatarBuilder` или Unity muscle solver; из COD Zombies перенесены принципы явной state machine, authored state variants и body-part collision/ragdoll. Root motion намеренно не переносится: положение модели остаётся авторитетным у игрового мозга обычного Sosig. Луч определения пола исключает слои штатных и кастомных hitbox, поэтому не может принять собственный ragdoll модели за поверхность. Кастомные hitbox копируют `PhysicMaterial`, `PMat.Def`, `PMat.MatDef` и condition со штатного SosigLink для мясных эффектов попадания.

## Сборка

Требуются установленные H3VR и BepInEx-профиль по путям пользователя.

```powershell
& 'C:\Users\pochk\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe' .\Tools\export-skinned-glb.mjs '.\metrocop\source\remade metro cop HL2.glb' .\Assets\metrocop\metrocop.cskmesh.gz
node .\Tools\export-retargeted-fbx-animation.mjs '.\animations\walking.fbx' '.\metrocop\source\remade metro cop HL2.glb' .\Assets\metrocop\mixamo_walk.csanim.gz
& 'C:\Users\pochk\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe' .\Tools\prepare-metrocop-textures.py
& .\Tools\build-plugin.ps1
& .\Tools\install-plugin.ps1
```

Готовая папка пакета: `dist\BepInEx\plugins\Pochk-CustomSosigReplacer`.

Путь установки: `E:\Games\r2mods\H3VR\profiles\Default\BepInEx\plugins\Pochk-CustomSosigReplacer`.

## Настройка

После первого запуска BepInEx создаст:

`BepInEx\config\pochk.h3vr.customsosigreplacer.cfg`

Основные параметры:

- `HeightMeters` — высота модели.
- `FeetBelowLowerLink` — вертикальное положение относительно нижнего link.
- `YawDegrees` — разворот модели, если она смотрит назад.
- `HideOriginal` — скрытие штатного тела/одежды.
- `Color`, `Metallic`, `Smoothness`, `UseNormalMaps` — материалы Metrocop.
- `EnableProceduralMotion`, `BobMeters`, `SwayDegrees`, `GaitDegrees` — skeletal locomotion; `BobMeters = 0` сохраняет высоту модели жёстко привязанной к полу.
- `MotionV3.DriveHeldWeaponsFromAnimation` — передавать authored-позиции рук штатным hand targets и настоящему оружию; по умолчанию включено.
- `MotionV3.HandTargetFollowSharpness` — скорость следования оружия за руками модели; по умолчанию `22`.

## Проверка в игре

Запустите любой режим или любого персонажа Take & Hold и создайте несколько разных Sosig, включая бронированных. В `BepInEx\LogOutput.log` должна появиться строка `Attached custom visual proxy ... via Sosig.Start`, а при смерти — строка об активации 11-part ragdoll. Категории `Custom Sosigs` после перезапуска игры больше быть не должно.

Сборка DLL и наличие файлов не доказывают правильный вид в VR. Необходима сцена-проверка масштаба, направления, оружия, смерти/ragdoll и нескольких типов Sosig.

## Источники архитектуры

- CoDZombies-H3VR использован как референс работы со стандартным `Sosig` и `SosigAPI`; его Zosig остаётся обычным `FistVR.Sosig`, а не системой произвольного skinned-меша.
- H3VR Modding Wiki использован для BepInEx/Harmony и структуры code mod.

См. `THIRD_PARTY_NOTICES.md`.
