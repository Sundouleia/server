using Grpc.Core;
using Grpc.Core.Interceptors;

/**
  SundouleiaFileHost - A distributed file hosting service.
  Copyright (C) 2025 Sundouleia Authors

  This program is free software: you can redistribute it and/or modify
  it under the terms of the GNU Affero General Public License as
  published by the Free Software Foundation, either version 3 of the
  License, or (at your option) any later version.

  This program is distributed in the hope that it will be useful,
  but WITHOUT ANY WARRANTY; without even the implied warranty of
  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
  GNU Affero General Public License for more details.

  You should have received a copy of the GNU Affero General Public License
  along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/

namespace SundouleiaFileHost;

class AuthInterceptor : Interceptor
{
	private readonly string _psk;

	public AuthInterceptor(string psk)
	{
		_psk = psk;
	}

	public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(TRequest request, ClientInterceptorContext<TRequest, TResponse> context, AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
	{
		var headers = new Metadata
		{
			{ "X-Api-Key", _psk }
		};
		var newOptions = context.Options.WithHeaders(headers);
		var newContext = new ClientInterceptorContext<TRequest, TResponse>(context.Method, context.Host, newOptions);
		return base.AsyncUnaryCall(request, newContext, continuation);
	}
}