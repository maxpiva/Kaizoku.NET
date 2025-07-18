// Debug script to check environment variables
console.log('NODE_ENV:', process.env.NODE_ENV);
console.log('NEXT_PUBLIC_KAIZOKU_BACKEND_URL:', process.env.NEXT_PUBLIC_KAIZOKU_BACKEND_URL);
console.log('All NEXT_PUBLIC_ vars:', Object.keys(process.env).filter(key => key.startsWith('NEXT_PUBLIC_')).map(key => `${key}: ${process.env[key]}`));
