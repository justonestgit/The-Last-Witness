-- The Last Witness: custom map script (Luau). Runs on every client, only in the CUSTOM game mode.
-- v0 (vertical slice): flickering lamps, blackouts, whispers, sanity on the wrist, the Witness stalking
-- and chasing you, a jumpscare when it catches you, and one tape.
-- Scene objects it expects are named TLW_* (see map/README.md).

local LIGHT_NAMES = {
	"TLW_Light_WallLamp_1", "TLW_Light_WallLamp_2", "TLW_Light_WallLamp_3", "TLW_Light_WallLamp_4",
	"TLW_Light_WallLamp_5", "TLW_Light_WallLamp_6", "TLW_Light_LampPost_1", "TLW_Light_LampPost_2",
	"TLW_Light_LampPost_3", "TLW_Light_CampfireGroup",
}
local POSES = { "rest", "run0", "run1", "run2", "run3", "run4", "run5", "run6", "run7", "reach", "tilt" }
local HEAD_UP = 0.25 -- head height above bodyPosition (shortcut: guessed, tune in game)
local SPAWN = Vec3.new(-39.1, 1.3, 13.2) -- just out of the lobby door, in the beach tunnel

-- ---------- helpers ----------

local timers = {}
function after(delay, fn)
	table.insert(timers, { at = os.clock() + delay, fn = fn })
end
local function runTimers()
	local now = os.clock()
	for i = #timers, 1, -1 do
		if now >= timers[i].at then
			local t = timers[i]
			table.remove(timers, i)
			t.fn()
		end
	end
end

local function rf(a, b)
	return a + math.random() * (b - a)
end

local function find(name)
	local go = GameObject.findGameObject(name)
	if go == nil then
		print("[TLW] missing object " .. name)
	end
	return go
end

local function headPos()
	return LocalPlayer.bodyPosition + Vec3.new(0, HEAD_UP, 0)
end

local function forward()
	return Vec3.rotate(Vec3.new(0, 0, 1), LocalPlayer.headRotation)
end

local function flat(v)
	return Vec3.new(v.x, 0, v.z)
end

-- Ground below a point (the Witness walks on whatever is under it).
local function ground(p)
	local hit = rayCast(p + Vec3.new(0, 3, 0), Vec3.new(0, -1, 0))
	if hit ~= nil then
		return hit.point
	end
	return p
end

-- ---------- scene ----------

local lamps = {}
for _, n in ipairs(LIGHT_NAMES) do
	local go = find(n)
	if go ~= nil then
		table.insert(lamps, { go = go, light = go:findComponent("Light"), base = 2, seed = math.random() * 100 })
	end
end

local sounds = {}
for _, n in ipairs({ "behind", "scare1", "scare2", "static", "steps", "heart", "drone" }) do
	local go = find("TLW_Snd_" .. n)
	if go ~= nil then
		sounds[n] = { go = go, src = go:findComponent("AudioSource") }
	end
end

local function play(name, pos, vol)
	local s = sounds[name]
	if s == nil then
		return
	end
	s.go.position = pos
	s.src:setVolume(vol or 1)
	s.src:play()
end

local witness = find("TLW_Witness")
local poseObjs = {}
for _, p in ipairs(POSES) do
	poseObjs[p] = witness ~= nil and witness:findChild("Pose_" .. p) or nil
end
local black = find("TLW_Black") -- inside-out black sphere around the head
local face = find("TLW_Face") -- the Witness's face for the jumpscare
local msg = find("TLW_Msg") -- text in front of the eyes
local wrist = find("TLW_Wrist") -- sanity text on the left wrist
local tape = find("TLW_Tape1")
local tapeText = find("TLW_TapeText")

-- ---------- state ----------

local sanity = 100
local lampLevel = 1 -- 0 = blackout, 1 = normal
local mode = "hidden" -- hidden | stalk | chase | scare
local modeUntil = 0
local nextEvent = os.clock() + 20
local pose = "rest"
local animT = 0
local msgUntil = 0
local blackUntil = 0

local function setPose(p)
	if p == pose then
		return
	end
	if poseObjs[pose] ~= nil then
		poseObjs[pose]:setActive(false)
	end
	if poseObjs[p] ~= nil then
		poseObjs[p]:setActive(true)
	end
	pose = p
end

local function showWitness(on)
	if witness ~= nil then
		witness:setActive(on) -- setActive, not setVisibility: the pose meshes are children
	end
end

local function say(text, seconds)
	if msg == nil then
		return
	end
	msg:setText(text)
	msg:setActive(true)
	msgUntil = os.clock() + seconds
end

local function blackout(seconds)
	if black ~= nil then
		black:setActive(true)
	end
	blackUntil = os.clock() + seconds
end

-- A spot around the player, out of view, at distance d.
local function spotBehind(d)
	local f = flat(forward()):normalize()
	local a = rf(-60, 60) * math.pi / 180
	local back = Vec3.new(-(f.x * math.cos(a) - f.z * math.sin(a)), 0, -(f.x * math.sin(a) + f.z * math.cos(a)))
	return ground(LocalPlayer.bodyPosition + back * d)
end

local function faceTowards(go, target)
	local d = flat(target - go.position)
	if d:length() > 0.01 then
		go.rotation = Quat.fromDirection(d:normalize())
	end
end

-- ---------- events ----------

local function flicker()
	lampLevel = 0.15
	play("static", headPos(), 0.5)
	after(rf(0.4, 1.2), function()
		lampLevel = 1
	end)
end

local function lightsOut()
	lampLevel = 0
	play("behind", headPos() + forward() * -1.5, 1)
	after(rf(3, 6), function()
		lampLevel = 1
	end)
end

local function stalk()
	if witness == nil then
		return
	end
	witness.position = spotBehind(rf(12, 18))
	faceTowards(witness, LocalPlayer.bodyPosition)
	setPose("tilt")
	showWitness(true)
	mode = "stalk"
	modeUntil = os.clock() + 20
	play("behind", witness.position, 0.8)
end

local function chase()
	if witness == nil then
		return
	end
	witness.position = spotBehind(rf(16, 24))
	showWitness(true)
	mode = "chase"
	modeUntil = os.clock() + rf(14, 22)
	lampLevel = 0.4
	play("heart", headPos(), 1)
	say("CORRA.", 2)
end

local function caught()
	mode = "scare"
	showWitness(false)
	lampLevel = 0
	if face ~= nil then
		face.position = headPos() + forward() * 0.35
		face.rotation = Quat.fromDirection(forward() * -1)
		face:setActive(true)
	end
	play(math.random() < 0.5 and "scare1" or "scare2", headPos(), 1)
	startVibration(true, 1, 1.2)
	startVibration(false, 1, 1.2)
	sanity = math.max(0, sanity - 20)
	after(1.1, function()
		if face ~= nil then
			face:setActive(false)
		end
		blackout(2.5)
		setPlayerPosition(SPAWN, false)
	end)
	after(3.6, function()
		lampLevel = 1
		mode = "hidden"
		say("ELE TE ACHOU.", 3)
	end)
end

local function event()
	local s = 1 - sanity / 100 -- 0 calm .. 1 mad
	local r = math.random()
	if r < 0.35 - 0.15 * s then
		flicker()
	elseif r < 0.55 then
		lightsOut()
	elseif r < 0.8 - 0.2 * s then
		stalk()
	else
		chase()
	end
	nextEvent = os.clock() + rf(25, 45) * (1 - 0.6 * s)
end

-- ---------- tape ----------

local TAPE1 = "FITA 1\n\nSe alguém achar isto: não fique depois que as luzes apagarem.\nEle não anda. Ele aparece."
if tape ~= nil then
	tape:onTouched(function()
		if tapeText == nil then
			return
		end
		tapeText:setText(TAPE1)
		tapeText:setActive(true)
		play("static", tape.position, 0.7)
		sanity = math.max(0, sanity - 10)
		after(9, function()
			tapeText:setActive(false)
			tape:setActive(false)
			chase()
		end)
	end)
end

-- ---------- per frame ----------

local function tickLamps(t)
	for _, l in ipairs(lamps) do
		local n = math.noise(t * 3 + l.seed, l.seed) -- -1..1
		local k = lampLevel * (0.85 + 0.15 * n)
		if lampLevel > 0.5 and math.random() < 0.002 then
			k = 0.1 -- a random sputter
		end
		if l.light ~= nil then
			l.light:setIntensity(l.base * k)
		end
	end
end

local function tickWitness(t, dt)
	if witness == nil or mode == "hidden" or mode == "scare" then
		return
	end
	local me = LocalPlayer.bodyPosition
	local d = flat(me - witness.position)
	local dist = d:length()
	if mode == "stalk" then
		faceTowards(witness, me)
		local toIt = flat(witness.position - me):normalize()
		local looking = flat(forward()):normalize():dot(toIt) > 0.85
		-- seen up close, or time's up: gone
		if (looking and dist < 9) or os.clock() > modeUntil then
			showWitness(false)
			mode = "hidden"
			play("static", witness.position, 0.6)
		end
	elseif mode == "chase" then
		local speed = 5.2 + 2.5 * (1 - sanity / 100)
		if dist > 0.01 then
			local step = d:normalize() * math.min(dist, speed * dt)
			witness.position = ground(witness.position + step)
		end
		faceTowards(witness, me)
		animT = animT + dt * 11
		setPose("run" .. tostring(math.floor(animT) % 8))
		if dist < 1.1 then
			caught()
		elseif os.clock() > modeUntil then
			showWitness(false)
			mode = "hidden"
			lampLevel = 1
			say("...", 1.5)
		end
	end
end

local function tickHud(t)
	if wrist ~= nil then
		wrist.position = LocalPlayer.leftHandPosition + Vec3.new(0, 0.06, 0)
		wrist.rotation = LocalPlayer.leftHandRotation
		wrist:setText("SANIDADE " .. tostring(math.floor(sanity)) .. "%")
	end
	if msg ~= nil and os.clock() < msgUntil then
		msg.position = headPos() + forward() * 1.2
		msg.rotation = Quat.fromDirection(forward())
	elseif msg ~= nil then
		msg:setActive(false)
	end
	if black ~= nil then
		black.position = headPos()
		if os.clock() > blackUntil then
			black:setActive(false)
		end
	end
end

function tick(dt)
	local t = os.clock()
	runTimers()
	sanity = math.max(0, sanity - dt / 6) -- about 10 minutes from 100 to 0
	if mode == "hidden" and t >= nextEvent then
		event()
	end
	tickLamps(t)
	tickWitness(t, dt)
	tickHud(t)
end

function onEvent(tag, data) end

-- start hidden and dark
showWitness(false)
for _, p in ipairs(POSES) do
	if poseObjs[p] ~= nil then
		poseObjs[p]:setActive(p == "rest")
	end
end
if face ~= nil then
	face:setActive(false)
end
if tapeText ~= nil then
	tapeText:setActive(false)
end
blackout(3)
after(3.2, function()
	say("A FLORESTA ESPERAVA VOCÊ.", 4)
end)
print("[TLW] script started")
