/mob/verb/examine(atom/thing as obj|mob in world)
	set category = null
	usr << "This is [thing]. [thing.desc]"

/turf
	icon = 'icons/turf.dmi'
	icon_state = "turf"
	layer = TURF_LAYER
	plane = -1

/turf/blue
	icon_state = "turf_blue"

/mob
	icon = 'icons/mob.dmi'
	icon_state = "mob"
	layer = MOB_LAYER
	plane = 5
	blend_mode = BLEND_OVERLAY
	name = "Square Man"
	desc = "Such a beautiful smile."
	gender = MALE
	see_invisible = 101

	New()
		..()
		loc = locate(5, 5, 1)
		//color = rgb(rand(0,255), rand(0,255), rand(0,255))

	Login()
		world.log << "login ran"
		src.client.screen += new /obj/order_test_item/plane_master //used for render tests

	verb/rotate()
		for(var/i in 1 to 8)
			src.transform = src.transform.Turn(45)
			sleep(2)

	verb/shake()
		animate(src, pixel_x = -4, time = 2)
		sleep(2)
		for (var/i in 1 to 3)
			animate(src, pixel_x = 4, time = 4)
			sleep(4)
			animate(src, pixel_x = -4, time = 4)
			sleep(4)
		animate(src, pixel_x = 0, time = 2)

	verb/tell_location()
		set name = "Tell Location"
		usr << "You are at ([x], [y], [z])"

	verb/say(message as text)
		var/list/viewers = viewers()

		for (var/mob/viewer in viewers)
			viewer << "[ckey] says: \"[message]\""

	verb/say_loud()
		var/msg = input("Please put the message you want to say loudly.", "Say Loud", "Hello!")
		world << "[ckey] says loudly: \"[msg]\""

	verb/move_up()
		step(src, UP)

	verb/move_down()
		step(src, DOWN)

	verb/roll_dice(dice as text)
		var/result = roll(dice)
		usr << "The total shown on the dice is: [result]"

	verb/test_alert()
		set category = "Test"
		alert(usr, "Prepare to die.")
		usr << "prompt done"

	verb/input_num()
		var/v = input("A") as num
		usr << "you entered [v]"

	verb/test_browse()
		set category = "Test"
		usr << browse({"
<!DOCTYPE html>
<html>
<head>
	<title>Foo</title>
	<style>
	body {
		background: red;
	}
	</style>
	<script>
	function foo(v) {
		document.getElementById("mark").innerHTML = v;
	}
	</script>
</head>
<body>
	<marquee id="mark">Honk</marquee>
	<a href="?honk=1">click me</a>
</body>
</html>"},"window=honk")

	verb/test_output()
		set category = "Test"
		usr << output("help sec griffing me", "honk.browser:foo")

	verb/demo_filters()
		set category = "Test"
		if(length(src.filters))
			src.filters = null
			usr << "Filters cleared"
		else
			var/selected = input("Pick a filter", "Choose a filter to apply (with demo settings)", null) as null|anything in list("alpha", "alpha-swap", "alpha-inverse", "alpha-both", "color", "outline", "greyscale", "blur", "outline/grey", "grey/outline", "drop_shadow")
			if(isnull(selected))
				src.filters = null
				usr << "No filter selected, filters cleared"
			switch(selected)
				if("alpha")
					src.filters = filter(type="alpha", icon=icon('icons/objects.dmi',"checker"))
				if("alpha-swap")
					src.filters = filter(type="alpha", icon=icon('icons/objects.dmi',"checker"), flags=MASK_SWAP)
				if("alpha-inverse")
					src.filters = filter(type="alpha", icon=icon('icons/objects.dmi',"checker"), flags=MASK_INVERSE)
				if("alpha-both")
					src.filters = filter(type="alpha", icon=icon('icons/objects.dmi',"checker"), flags=MASK_INVERSE|MASK_SWAP)
				if("outline")
					src.filters = filter(type="outline", size=1, color=rgb(255,0,0))
				if("greyscale")
					src.filters = filter(type="greyscale")
				if("blur")
					src.filters = filter(type="blur", size=2)
				if("outline/grey")
					src.filters = list(filter(type="outline", size=1, color=rgb(255,0,0)), filter(type="greyscale"))
				if("grey/outline")
					src.filters = list(filter(type="greyscale"), filter(type="outline", size=1, color=rgb(255,0,0)))
				if("color")
					src.filters = filter(type="color", color=list("#de0000","#000000","#00ad00"))
				if("drop_shadow")
					src.filters = filter(type="drop_shadow", size=2)
			usr << "Applied [selected] filter"

	verb/toggle_see_invisibility()
		if(src.see_invisible == 0)
			src.see_invisible = 101
			usr << "now seeing invisible things"
		else
			src.see_invisible = 0
			usr << "now blind to invisible things"

	verb/add_client_image()
		var/image/i = image(icon = 'icons/hanoi.dmi', icon_state="8")
		i.loc = src
		src.client.images += i
		spawn(20)
			src.client.images.Remove(i)
			del(i)

/mob/Stat()
	if (statpanel("Status"))
		stat("tick_usage", world.tick_usage)
		stat("time", world.time)

/world/New()
	..()
	world.log << "World loaded!"
