#pragma once

#include <functional>
#include <vector>
#include <memory>

// Emulates C# delegates; adding/removing callbacks from a list
template<typename... Params>
struct Delegate
{
	typedef Delegate<Params...> Container;
	typedef std::function<void(Params...)> Function;

	struct Item
	{
		Function mFn;
		Item(const Function& fn)
			: mFn(fn) { }
	};

protected:
	std::shared_ptr<Container*> mContainer;
	std::vector<std::shared_ptr<Item>> mCallbacks;

public:
	typedef Function Function;

	Delegate()
		: mContainer(std::make_shared<Container*>(this))
	{ }
	void Remove(const std::shared_ptr<Item>& item)
	{
		for (int i = (int)mCallbacks.size() - 1; i >= 0; --i)
		{
			if (mCallbacks[i].get() == item.get()) mCallbacks.erase(mCallbacks.begin() + i);
		}
	}

	void Invoke(Params... params)
	{
		for (auto& callback : mCallbacks)
		{
			callback->mFn(params...);
		}
	}

	class Reference
	{
		std::weak_ptr<Container*> mContainer;
		std::shared_ptr<Item> mItem;
	public:
		Reference() { }
		Reference(Reference&& other)
		{
			mContainer.swap(other.mContainer);
			mItem.swap(other.mItem);
		}
		Reference(const std::shared_ptr<Item>& item, const std::shared_ptr<Container*>& container)
			: mItem(item), mContainer(container) { }
		Reference& operator=(Reference&& other)
		{
			mContainer.swap(other.mContainer);
			mItem.swap(other.mItem);
			return *this;
		}
		~Reference()
		{
			if (auto item = mContainer.lock()) {
				(*item)->Remove(mItem);
			}
		}
	};

	Reference Add(const Function& fn)
	{
		auto item = std::make_shared<Item>(fn);
		mCallbacks.push_back(item);
		return Reference(item, mContainer);
	}

};
