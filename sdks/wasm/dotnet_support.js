
var DotNetSupportLib = {
	$DOTNET: {
		_dotnet_get_global: function() {
			function testGlobal(obj) {
				obj['___dotnet_global___'] = obj;
				var success = typeof ___dotnet_global___ === 'object' && obj['___dotnet_global___'] === obj;
				if (!success) {
					delete obj['___dotnet_global___'];
				}
				return success;
			}
			if (typeof ___dotnet_global___ === 'object') {
				return ___dotnet_global___;
			}
			if (typeof global === 'object' && testGlobal(global)) {
				___dotnet_global___ = global;
			} else if (typeof window === 'object' && testGlobal(window)) {
				___dotnet_global___ = window;
			}
			if (typeof ___dotnet_global___ === 'object') {
				return ___dotnet_global___;
			}
			throw Error('unable to get DotNet global object.');
		},
		//FIXME this is wastefull, we could remove the temp malloc by going the UTF16 route
		//FIXME this is unsafe, cuz raw objects could be GC'd.
		conv_string: function (mono_obj) {
			if (mono_obj == 0)
				return null;

			if (!this.mono_string_get_utf16)
				this.mono_string_get_utf16 = Module.cwrap ('mono_wasm_string_get_utf16', 'number', ['number']);

			var raw = this.mono_string_get_utf16 (mono_obj);
			var res = Module.UTF16ToString (raw);
			Module._free (raw);

			return res;
		},		
	},
	mono_wasm_invoke_js_marshalled: function(exceptionMessage, asyncHandleLongPtr, functionName, argsJson) {

		var mono_string = DOTNET._dotnet_get_global()._mono_string_cached
			|| (DOTNET._dotnet_get_global()._mono_string_cached = Module.cwrap('mono_wasm_string_from_js', 'number', ['string']));
	
		try {
			// Passing a .NET long into JS via Emscripten is tricky. The method here is to pass
			// as pointer to the long, then combine two reads from the HEAPU32 array.
			// Even though JS numbers can't represent the full range of a .NET long, it's OK
			// because we'll never exceed Number.MAX_SAFE_INTEGER (2^53 - 1) in this case.
			//var u32Index = $1 >> 2;
			var u32Index = asyncHandleLongPtr >> 2;
			var asyncHandleJsNumber = Module.HEAPU32[u32Index + 1]*4294967296 + Module.HEAPU32[u32Index];

			// var funcNameJsString = UTF8ToString (functionName);
			// var argsJsonJsString = argsJson && UTF8ToString (argsJson);
			var funcNameJsString = DOTNET.conv_string(functionName);
			var argsJsonJsString = argsJson && DOTNET.conv_string (argsJson);

			var dotNetExports = DOTNET._dotnet_get_global().DotNet;
			if (!dotNetExports) {
				throw new Error('The Microsoft.JSInterop.js library is not loaded.');
			}

			if (asyncHandleJsNumber) {
				dotNetExports.jsCallDispatcher.beginInvokeJSFromDotNet(asyncHandleJsNumber, funcNameJsString, argsJsonJsString);
				return 0;
			} else {
				var resultJson = dotNetExports.jsCallDispatcher.invokeJSFromDotNet(funcNameJsString, argsJsonJsString);
				return resultJson === null ? 0 : mono_string(resultJson);
			}
		} catch (ex) {
			var exceptionJsString = ex.message + '\n' + ex.stack;
			var exceptionSystemString = mono_string(exceptionJsString);
			setValue (exceptionMessage, exceptionSystemString, 'i32'); // *exceptionMessage = exceptionSystemString;
			return 0;
		}
	},
	mono_wasm_invoke_js_unmarshalled: function(exceptionMessage, funcName, arg0, arg1, arg2)	{
		try {
			// Get the function you're trying to invoke
			var funcNameJsString = DOTNET.conv_string(funcName);
			var dotNetExports = DOTNET._dotnet_get_global().DotNet;
			if (!dotNetExports) {
				throw new Error('The Microsoft.JSInterop.js library is not loaded.');
			}
			var funcInstance = dotNetExports.jsCallDispatcher.findJSFunction(funcNameJsString);

			return funcInstance.call(null, arg0, arg1, arg2);
		} catch (ex) {
			var exceptionJsString = ex.message + '\n' + ex.stack;
			var mono_string = Module.cwrap('mono_wasm_string_from_js', 'number', ['string']); // TODO: Cache
			var exceptionSystemString = mono_string(exceptionJsString);
			setValue (exceptionMessage, exceptionSystemString, 'i32'); // *exceptionMessage = exceptionSystemString;
			return 0;
		}
	}
	

};

autoAddDeps(DotNetSupportLib, '$DOTNET')
mergeInto(LibraryManager.library, DotNetSupportLib)

